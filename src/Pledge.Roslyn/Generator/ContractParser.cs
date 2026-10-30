using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Reads the leading Pledge calls out of a method body and builds the model the emitter works from.
internal static class ContractParser
{
    public const string PledgeTypeName = "PledgeContracts.Pledge";

    public static ContractedMethod? Parse(MethodDeclarationSyntax declaration, SemanticModel model, CancellationToken cancellationToken)
    {
        if (declaration.Body is null || model.GetDeclaredSymbol(declaration, cancellationToken) is not { } method)
        {
            return null;
        }

        var compilation = model.Compilation;
        var pledgeType = compilation.GetTypeByMetadataName(PledgeTypeName);
        if (pledgeType is null)
        {
            return null;
        }

        var (asyncKind, resultType) = Classify(method, compilation);
        var problems = new List<string>();
        var contracts = new List<ContractModel>();
        var snapshots = new List<SnapshotModel>();
        var normalizer = new ContractTextNormalizer(model, pledgeType);
        var rewriter = new ContractExpressionRewriter(model, pledgeType, resultType, normalizer, snapshots, problems);
        var cancellationType = compilation.GetTypeByMetadataName("System.OperationCanceledException");
        var onThrowCoversCancellation = false;

        foreach (var statement in declaration.Body.Statements)
        {
            if (!ContractSyntax.IsContractCall(statement, out var invocation))
            {
                break;
            }

            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol call
                || !SymbolEqualityComparer.Default.Equals(call.ContainingType, pledgeType))
            {
                break;
            }

            var kind = call.Name switch
            {
                "Requires" => ContractKind.Precondition,
                "Ensures" => ContractKind.Postcondition,
                _ => ContractKind.PostconditionOnThrow,
            };

            string? exceptionType = null;
            var exceptionDepth = 0;
            if (kind == ContractKind.PostconditionOnThrow && call.TypeArguments.Length == 1)
            {
                var thrown = call.TypeArguments[0];
                exceptionType = thrown.ToDisplayString(DisplayFormats.Type);
                exceptionDepth = Depth(thrown);
                onThrowCoversCancellation |= cancellationType is not null && DerivesFrom(thrown, cancellationType);
            }

            var condition = Argument(invocation, call, "condition");
            if (condition is null)
            {
                continue;
            }

            rewriter.Kind = kind;
            var message = Argument(invocation, call, "message");
            contracts.Add(new ContractModel(
                Index: contracts.Count,
                Kind: kind,
                Text: normalizer.Normalize(condition),
                Expression: rewriter.Lift(condition),
                Message: message is null ? null : rewriter.Lift(message),
                Site: SourceSite.Of(condition),
                ExceptionType: exceptionType,
                ExceptionDepth: exceptionDepth,
                CannotThrow: ThrowAnalysis.CannotThrow(condition, model, pledgeType, kind, cancellationToken)));
        }

        if (contracts.Count == 0 && problems.Count == 0)
        {
            return null;
        }

        CheckTarget(method, asyncKind, contracts, snapshots, problems);

        var key = method.ToDisplayString(DisplayFormats.MethodKey);
        var baseName = Sanitize(method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "_" + method.Name) + "_" + Fnv1a(key);

        return new ContractedMethod(
            Key: key,
            HintName: baseName + ".g.cs",
            ClassName: baseName,
            ContainingType: method.ContainingType.ToDisplayString(DisplayFormats.Type),
            MemberName: method.ContainingType.Name + "." + method.Name,
            MethodName: method.Name,
            Signature: method.ToDisplayString(DisplayFormats.Signature),
            IsStatic: method.IsStatic,
            ReturnsVoid: method.ReturnsVoid,
            ReturnType: method.ReturnType.ToDisplayString(DisplayFormats.Type),
            AsyncKind: asyncKind,
            ResultType: resultType?.ToDisplayString(DisplayFormats.Type),
            ResultTypeOf: resultType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            OnThrowCoversCancellation: onThrowCoversCancellation,
            Parameters: method.Parameters.Select(Parameter).ToEquatableArray(),
            Usings: CollectUsings(declaration, model, cancellationToken).ToEquatableArray(),
            Contracts: contracts.ToEquatableArray(),
            Snapshots: snapshots.ToEquatableArray(),
            Problems: problems.Distinct().ToEquatableArray(),
            HasErrors: rewriter.HasErrors,
            ProbeUnsupportedReason: ProbeUnsupportedReason(method, resultType),
            Site: SourceSite.Of(declaration.Identifier.Parent ?? declaration));
    }

    private static ParameterModel Parameter(IParameterSymbol parameter) => new(
        parameter.Name,
        parameter.Type.ToDisplayString(DisplayFormats.Type),
        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        parameter.RefKind,
        AllowsNull: parameter.Type.IsReferenceType
            ? parameter.NullableAnnotation == NullableAnnotation.Annotated
            : parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

    // A probe passes arguments through object?[] and returns the result boxed, so types that cannot be boxed or
    // named in typeof cannot be probed. The member is still registered, with this as the reason.
    private static string? ProbeUnsupportedReason(IMethodSymbol method, ITypeSymbol? resultType)
    {
        foreach (var parameter in method.Parameters)
        {
            if (!CanBox(parameter.Type))
            {
                return $"parameter '{parameter.Name}' is a {parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}, which cannot be passed as an object";
            }
        }

        if (resultType is not null && !CanBox(resultType))
        {
            return $"the result is a {resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}, which cannot be returned as an object";
        }

        return null;

        static bool CanBox(ITypeSymbol type) => !type.IsRefLikeType && type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer);
    }

    // What the wrapper awaits, if anything, and the type Result<T>() stands for.
    public static (AsyncKind Kind, ITypeSymbol? ResultType) Classify(IMethodSymbol method, Compilation compilation)
    {
        if (method.ReturnsVoid)
        {
            return (AsyncKind.None, null);
        }

        var returnType = method.ReturnType;
        var definition = returnType.OriginalDefinition;
        if (Is(definition, "System.Threading.Tasks.Task"))
        {
            return (AsyncKind.Task, null);
        }

        if (Is(definition, "System.Threading.Tasks.Task`1"))
        {
            return (AsyncKind.TaskOfResult, ((INamedTypeSymbol)returnType).TypeArguments[0]);
        }

        if (Is(definition, "System.Threading.Tasks.ValueTask"))
        {
            return (AsyncKind.ValueTask, null);
        }

        if (Is(definition, "System.Threading.Tasks.ValueTask`1"))
        {
            return (AsyncKind.ValueTaskOfResult, ((INamedTypeSymbol)returnType).TypeArguments[0]);
        }

        return (AsyncKind.None, returnType);

        bool Is(ITypeSymbol candidate, string metadataName) =>
            SymbolEqualityComparer.Default.Equals(candidate, compilation.GetTypeByMetadataName(metadataName));
    }

    public static ExpressionSyntax? Argument(InvocationExpressionSyntax invocation, IMethodSymbol method, string parameterName)
    {
        var arguments = invocation.ArgumentList.Arguments;
        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == parameterName)
            {
                return argument.Expression;
            }
        }

        for (var i = 0; i < method.Parameters.Length && i < arguments.Count; i++)
        {
            if (method.Parameters[i].Name == parameterName)
            {
                return arguments[i].NameColon is null ? arguments[i].Expression : null;
            }
        }

        return null;
    }

    private static void CheckTarget(IMethodSymbol method, AsyncKind asyncKind, List<ContractModel> contracts, List<SnapshotModel> snapshots, List<string> problems)
    {
        if (method.MethodKind != MethodKind.Ordinary)
        {
            problems.Add("only ordinary methods are supported yet");
        }

        if (method.IsGenericMethod)
        {
            problems.Add("generic methods are not supported yet");
        }

        if (method.ContainingType.IsGenericType)
        {
            problems.Add("methods on generic types are not supported yet");
        }

        if (method.ContainingType.TypeKind != TypeKind.Class)
        {
            problems.Add("only methods on classes are supported yet; structs and interfaces come later");
        }

        if (method.IsExtensionMethod)
        {
            problems.Add("extension methods are not supported yet");
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            problems.Add("ref returns are not supported");
        }

        var wrapsBody = snapshots.Count > 0 || contracts.Any(contract => contract.Kind != ContractKind.Precondition);
        if (wrapsBody && method.IsAsync && asyncKind == AsyncKind.None)
        {
            problems.Add(method.ReturnsVoid
                ? "async void methods cannot carry postconditions: there is no completion to check them after"
                : "postconditions on async methods are supported for Task, Task<T>, ValueTask and ValueTask<T> only");
        }

        for (ISymbol? current = method; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                problems.Add($"'{current.Name}' is not visible outside its type, so the generated check cannot call it; contracts need internal or public members");
                break;
            }
        }
    }

    private static int Depth(ITypeSymbol type)
    {
        var depth = 0;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    // The generated file is elsewhere, so the author's usings (resolved, so relative names survive the move) and the
    // enclosing namespaces are re-imported. Global usings already apply compilation-wide.
    private static List<string> CollectUsings(MethodDeclarationSyntax declaration, SemanticModel model, CancellationToken cancellationToken)
    {
        var usings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ancestor in declaration.Ancestors())
        {
            var directives = ancestor switch
            {
                CompilationUnitSyntax unit => unit.Usings,
                BaseNamespaceDeclarationSyntax ns => ns.Usings,
                _ => default,
            };

            foreach (var directive in directives)
            {
                if (directive.GlobalKeyword != default || directive.Name is null)
                {
                    continue;
                }

                var target = model.GetSymbolInfo(directive.Name, cancellationToken).Symbol;
                var resolved = target switch
                {
                    INamespaceSymbol ns when !ns.IsGlobalNamespace => "global::" + ns.ToDisplayString(),
                    ITypeSymbol type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    _ => null,
                };

                if (resolved is null)
                {
                    continue;
                }

                var text = directive.Alias is { } alias
                    ? $"using {alias.Name.Identifier.ValueText} = {resolved};"
                    : directive.StaticKeyword != default ? $"using static {resolved};" : $"using {resolved};";

                if (seen.Add(text))
                {
                    usings.Add(text);
                }
            }

            if (ancestor is BaseNamespaceDeclarationSyntax enclosing && model.GetDeclaredSymbol(enclosing, cancellationToken) is INamespaceSymbol declared)
            {
                for (var ns = declared; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
                {
                    var text = $"using global::{ns.ToDisplayString()};";
                    if (seen.Add(text))
                    {
                        usings.Add(text);
                    }
                }
            }
        }

        return usings;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private static string Fnv1a(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619u;
            }

            return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
