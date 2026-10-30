using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using PledgeContracts.Roslyn.Generator;

namespace PledgeContracts.Roslyn.Analyzers;

// PLEDGE006 where a contracted method is reached without its interceptor. A method group turned into a delegate is
// never intercepted, and a call through an interface or a base method binds to that member, not to the contracted
// implementation it may run. The generator reports the other case, a contracted method nothing calls. Like the
// generator, this stays silent when every kind of contract is Ignore, since nothing is checked anywhere then.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UncoveredCallAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.DelegateNotChecked, Diagnostics.DispatchNotChecked);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(ContractParser.PledgeTypeName) is null)
            {
                return;
            }

            var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            var modes = PledgeModes.Parse(Option(options, PledgeModes.ModeProperty), Option(options, PledgeModes.PreconditionsProperty), Option(options, PledgeModes.PostconditionsProperty));
            if (modes.Preconditions == PledgeMode.Ignore && modes.Postconditions == PledgeMode.Ignore)
            {
                return;
            }

            var contracted = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
            var cancellationToken = start.CancellationToken;
            var dispatch = new Lazy<ImmutableDictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>>>(
                () => FindDispatchTargets(start.Compilation, method => IsContracted(method, contracted, cancellationToken), cancellationToken));

            start.RegisterOperationAction(context => AnalyzeReference(context, contracted), OperationKind.MethodReference);
            start.RegisterOperationAction(context => AnalyzeInvocation(context, dispatch), OperationKind.Invocation);
        });
    }

    private static void AnalyzeReference(OperationAnalysisContext context, ConcurrentDictionary<IMethodSymbol, bool> contracted)
    {
        var reference = (IMethodReferenceOperation)context.Operation;
        if (IsContracted(reference.Method, contracted, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.DelegateNotChecked, reference.Syntax.GetLocation(), Name(reference.Method)));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, Lazy<ImmutableDictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>>> dispatch)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!invocation.IsVirtual || invocation.Instance?.Type is not { } receiverType)
        {
            return;
        }

        var target = invocation.TargetMethod.OriginalDefinition;
        if (!dispatch.Value.TryGetValue(target, out var implementations))
        {
            return;
        }

        var location = invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : invocation.Syntax.GetLocation();
        foreach (var implementation in implementations)
        {
            // Only an implementation the receiver can actually be: a sibling class's override is not reachable.
            if (!SymbolEqualityComparer.Default.Equals(implementation, target) && DerivesFrom(implementation.ContainingType, receiverType))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.DispatchNotChecked, location, Name(target), Name(implementation)));
            }
        }
    }

    // Every interface member and base method in this compilation's reach that a contracted method of a source type
    // implements or overrides, mapped to those contracted methods. Built once per compilation, on the first call
    // that dispatches virtually.
    private static ImmutableDictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>> FindDispatchTargets(Compilation compilation, Func<IMethodSymbol, bool> isContracted, CancellationToken cancellationToken)
    {
        var targets = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        var types = new Stack<INamespaceOrTypeSymbol>();
        types.Push(compilation.Assembly.GlobalNamespace);
        while (types.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in types.Pop().GetMembers())
            {
                if (member is INamespaceOrTypeSymbol container)
                {
                    types.Push(container);
                }

                if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method
                    || !(method.IsOverride || method.ContainingType.AllInterfaces.Length > 0)
                    || !isContracted(method))
                {
                    continue;
                }

                for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
                {
                    Add(targets, overridden.OriginalDefinition, method);
                }

                foreach (var contract in method.ContainingType.AllInterfaces)
                {
                    foreach (var interfaceMethod in contract.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(interfaceMethod), method))
                        {
                            Add(targets, interfaceMethod.OriginalDefinition, method);
                        }
                    }
                }
            }
        }

        return targets.ToImmutableDictionary<KeyValuePair<IMethodSymbol, List<IMethodSymbol>>, IMethodSymbol, ImmutableArray<IMethodSymbol>>(
            pair => pair.Key, pair => [.. pair.Value], SymbolEqualityComparer.Default);

        static void Add(Dictionary<IMethodSymbol, List<IMethodSymbol>> targets, IMethodSymbol target, IMethodSymbol implementation)
        {
            if (!targets.TryGetValue(target, out var implementations))
            {
                targets[target] = implementations = [];
            }

            implementations.Add(implementation);
        }
    }

    // The generator's own test for a contracted method, so the two agree on which methods have contracts.
    private static bool IsContracted(IMethodSymbol method, ConcurrentDictionary<IMethodSymbol, bool> cache, CancellationToken cancellationToken)
    {
        var definition = method.OriginalDefinition;
        definition = definition.PartialImplementationPart ?? definition;
        return cache.GetOrAdd(definition, candidate =>
            candidate.DeclaringSyntaxReferences.Length > 0
            && candidate.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration
            && ContractSyntax.IsCandidateDeclaration(declaration));
    }

    private static bool DerivesFrom(INamedTypeSymbol type, ITypeSymbol receiverType)
    {
        var receiver = receiverType.OriginalDefinition;
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, receiver))
            {
                return true;
            }
        }

        return type.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, receiver));
    }

    private static string Name(IMethodSymbol method) => method.ContainingType.Name + "." + method.Name;

    private static string? Option(AnalyzerConfigOptions options, string key) => options.TryGetValue(key, out var value) ? value : null;
}
