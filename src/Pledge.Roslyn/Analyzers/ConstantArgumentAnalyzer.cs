using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using PledgeContracts.Roslyn.Generator;

namespace PledgeContracts.Roslyn.Analyzers;

// PLEDGE008: a call that passes constants a precondition rejects, such as Withdraw(-5). Only constants are
// evaluated; anything else in the condition is unknown and the call is left alone. Preconditions are read from
// methods declared in this compilation; reading them from referenced assemblies' metadata is v1.0.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstantArgumentAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.ConstantViolatesPrecondition);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(ContractParser.PledgeTypeName) is not { } pledge)
            {
                return;
            }

            var cache = new ConcurrentDictionary<IMethodSymbol, ImmutableArray<Precondition>>(SymbolEqualityComparer.Default);
            start.RegisterOperationAction(context => Analyze(context, pledge, cache), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol pledge, ConcurrentDictionary<IMethodSymbol, ImmutableArray<Precondition>> cache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.ReducedFrom is not null || !invocation.Arguments.Any(static argument => argument.Value.ConstantValue.HasValue))
        {
            return;
        }

        var target = invocation.TargetMethod.OriginalDefinition;
        target = target.PartialImplementationPart ?? target;
        var preconditions = cache.GetOrAdd(target, method => Collect(method, context.Compilation, pledge, context.CancellationToken));
        if (preconditions.IsEmpty)
        {
            return;
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var arguments = new Dictionary<string, IArgumentOperation>(StringComparer.Ordinal);
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is { } parameter && argument.ArgumentKind is ArgumentKind.Explicit or ArgumentKind.DefaultValue && argument.Value.ConstantValue.HasValue)
            {
                values[parameter.Name] = argument.Value.ConstantValue.Value;
                arguments[parameter.Name] = argument;
            }
        }

        foreach (var precondition in preconditions)
        {
            var used = new List<string>();
            if (PreconditionEvaluator.Evaluate(precondition.Condition, precondition.Model, values, used) != false || used.Count == 0)
            {
                continue;
            }

            var location = arguments[used[0]] is { ArgumentKind: ArgumentKind.Explicit } explicitArgument ? explicitArgument.Value.Syntax.GetLocation() : invocation.Syntax.GetLocation();
            var summary = string.Join(", ", used.Select(name => $"{name}: {Format(values[name])}"));
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ConstantViolatesPrecondition,
                location,
                additionalLocations: [precondition.Condition.GetLocation()],
                summary,
                precondition.Text,
                target.ContainingType.Name + "." + target.Name));
        }
    }

    private static ImmutableArray<Precondition> Collect(IMethodSymbol method, Compilation compilation, INamedTypeSymbol pledge, CancellationToken cancellationToken)
    {
        if (method.DeclaringSyntaxReferences.Length == 0
            || method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax { Body: { } body } declaration
            || !ContractSyntax.IsCandidateDeclaration(declaration))
        {
            return ImmutableArray<Precondition>.Empty;
        }

        // The declaring method may live in another tree than the call. One model per contracted method is cached for the
        // whole compilation, so this is not the per-node churn RS1030 guards against.
#pragma warning disable RS1030
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030
        var normalizer = new ContractTextNormalizer(model, pledge);
        var builder = ImmutableArray.CreateBuilder<Precondition>();
        foreach (var statement in body.Statements)
        {
            if (!ContractSyntax.IsContractCall(statement, out var invocation)
                || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol call
                || !SymbolEqualityComparer.Default.Equals(call.ContainingType, pledge))
            {
                break;
            }

            if (call.Name == "Requires" && ContractParser.Argument(invocation, call, "condition") is { } condition)
            {
                builder.Add(new Precondition(condition, normalizer.Normalize(condition), model));
            }
        }

        return builder.ToImmutable();
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        char c => "'" + c + "'",
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private sealed record Precondition(ExpressionSyntax Condition, string Text, SemanticModel Model);
}
