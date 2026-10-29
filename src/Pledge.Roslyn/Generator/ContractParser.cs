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

        var pledgeType = model.Compilation.GetTypeByMetadataName(PledgeTypeName);
        if (pledgeType is null)
        {
            return null;
        }

        var problems = new List<string>();
        var contracts = new List<ContractModel>();
        var rewriter = new ContractExpressionRewriter(model, problems);

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

            if (call.Name != "Requires")
            {
                problems.Add($"Pledge.{call.Name} is not supported yet; postconditions land in a later release");
                continue;
            }

            var condition = Argument(invocation, call, "condition");
            if (condition is null)
            {
                continue;
            }

            var message = Argument(invocation, call, "message");
            contracts.Add(new ContractModel(
                ContractKind.Precondition,
                Text: condition.NormalizeWhitespace().ToString(),
                Expression: Lift(rewriter, condition),
                Message: message is null ? null : Lift(rewriter, message),
                SourceSite.Of(condition)));
        }

        if (contracts.Count == 0 && problems.Count == 0)
        {
            return null;
        }

        CheckTarget(method, problems);

        var key = method.ToDisplayString(DisplayFormats.MethodKey);
        var baseName = Sanitize(method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "_" + method.Name) + "_" + Fnv1a(key);

        return new ContractedMethod(
            Key: key,
            HintName: baseName + ".g.cs",
            ClassName: baseName,
            ContainingType: method.ContainingType.ToDisplayString(DisplayFormats.Type),
            MemberName: method.ContainingType.Name + "." + method.Name,
            MethodName: method.Name,
            IsStatic: method.IsStatic,
            ReturnsVoid: method.ReturnsVoid,
            ReturnType: method.ReturnType.ToDisplayString(DisplayFormats.Type),
            Parameters: method.Parameters.Select(p => new ParameterModel(p.Name, p.Type.ToDisplayString(DisplayFormats.Type), p.RefKind)).ToEquatableArray(),
            Usings: CollectUsings(declaration, model, cancellationToken).ToEquatableArray(),
            Contracts: contracts.ToEquatableArray(),
            Problems: problems.Distinct().ToEquatableArray(),
            Site: SourceSite.Of(declaration.Identifier.Parent ?? declaration));
    }

    private static ExpressionSyntax? Argument(InvocationExpressionSyntax invocation, IMethodSymbol method, string parameterName)
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

    private static string Lift(ContractExpressionRewriter rewriter, ExpressionSyntax expression) =>
        rewriter.Visit(expression)!.NormalizeWhitespace().ToFullString();

    private static void CheckTarget(IMethodSymbol method, List<string> problems)
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

        for (ISymbol? current = method; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                problems.Add($"'{current.Name}' is not visible outside its type, so the generated check cannot call it; contracts need internal or public members");
                break;
            }
        }
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
