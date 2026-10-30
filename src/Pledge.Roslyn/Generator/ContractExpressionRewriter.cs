using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Rewrites a contract expression so it means the same thing inside the generated wrapper, where the receiver is a
// parameter named @this and none of the author's usings apply: implicit instance members become @this.Member,
// static members and types are spelled out with global::, parameters and lambda locals stay as they are.
// Pledge.Result<T>() becomes the __result local and Pledge.Old(x) becomes a snapshot local taken before the body.
// Anything the wrapper cannot do (private members, a type it cannot name) is recorded as a problem that the
// generator reports as PLEDGE009. Misuse of Result and Old is the analyzers' error (PLEDGE002, PLEDGE003); the
// rewriter only notes it in HasErrors so no wrapper is emitted for the method.
internal sealed class ContractExpressionRewriter(
    SemanticModel model,
    INamedTypeSymbol pledgeType,
    ITypeSymbol? resultType,
    ContractTextNormalizer normalizer,
    List<SnapshotModel> snapshots,
    List<string> problems) : CSharpSyntaxRewriter
{
    private static readonly IdentifierNameSyntax _receiver = SyntaxFactory.IdentifierName("@this");
    private static readonly IdentifierNameSyntax _result = SyntaxFactory.IdentifierName("__result");

    private bool _insideOld;

    public ContractKind Kind { get; set; }

    public bool HasErrors { get; private set; }

    public string Lift(ExpressionSyntax expression) => Visit(expression)!.NormalizeWhitespace().ToFullString();

    public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node) => _receiver.WithTriviaFrom(node);

    public override SyntaxNode? VisitBaseExpression(BaseExpressionSyntax node)
    {
        problems.Add("'base' cannot be used in a contract expression");
        return node;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (model.GetSymbolInfo(node).Symbol is IMethodSymbol method && SymbolEqualityComparer.Default.Equals(method.ContainingType, pledgeType))
        {
            switch (method.Name)
            {
                case "Result":
                    return RewriteResult(node, method);
                case "Old":
                    return RewriteOld(node);
                default:
                    problems.Add($"Pledge.{method.Name} cannot be nested inside a contract expression");
                    return node;
            }
        }

        return base.VisitInvocationExpression(node);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => Qualify(node, node) ?? node;

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
    {
        var visited = (GenericNameSyntax)base.VisitGenericName(node)!;
        return Qualify(node, visited) ?? visited;
    }

    private IdentifierNameSyntax RewriteResult(InvocationExpressionSyntax node, IMethodSymbol method)
    {
        if (_insideOld
            || Kind != ContractKind.Postcondition
            || resultType is null
            || (method.TypeArguments.Length == 1 && !SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], resultType)))
        {
            HasErrors = true;
        }

        return _result.WithTriviaFrom(node);
    }

    private SyntaxNode RewriteOld(InvocationExpressionSyntax node)
    {
        if (Kind == ContractKind.Precondition)
        {
            HasErrors = true;
        }

        if (node.ArgumentList.Arguments.Count != 1)
        {
            return node;
        }

        var argument = node.ArgumentList.Arguments[0].Expression;
        var text = normalizer.Normalize(argument);
        foreach (var existing in snapshots)
        {
            if (existing.Text == text)
            {
                return SyntaxFactory.IdentifierName(existing.Name).WithTriviaFrom(node);
            }
        }

        var type = model.GetTypeInfo(argument).Type;
        if (type is null || type.TypeKind == TypeKind.Error || type.IsAnonymousType)
        {
            problems.Add($"the type of Pledge.Old({text}) cannot be named in generated code");
        }

        var wasInsideOld = _insideOld;
        _insideOld = true;
        var expression = Lift(argument);
        _insideOld = wasInsideOld;

        var name = "__old" + snapshots.Count;
        snapshots.Add(new SnapshotModel(name, type?.ToDisplayString(DisplayFormats.Type) ?? "object", expression, text, SourceSite.Of(argument)));
        return SyntaxFactory.IdentifierName(name).WithTriviaFrom(node);
    }

    private SyntaxNode? Qualify(SimpleNameSyntax original, SimpleNameSyntax current)
    {
        if (IsNameOfAccess(original))
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(original).Symbol;
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        switch (symbol)
        {
            case null:
            case IParameterSymbol:
            case ILocalSymbol:
            case IRangeVariableSymbol:
            case IDiscardSymbol:
            case ILabelSymbol:
            case ITypeParameterSymbol:
                return null;

            case INamespaceSymbol { IsGlobalNamespace: true }:
                return null;

            case INamespaceSymbol ns:
                return SyntaxFactory.ParseName("global::" + ns.ToDisplayString()).WithTriviaFrom(original);

            case ITypeSymbol type:
                EnsureReachable(type);
                return SyntaxFactory.ParseTypeName(type.ToDisplayString(DisplayFormats.Type)).WithTriviaFrom(original);

            case IMethodSymbol { MethodKind: MethodKind.LocalFunction }:
                problems.Add($"local function '{symbol.Name}' cannot be used in a contract expression");
                return null;

            case IMethodSymbol:
            case IFieldSymbol:
            case IPropertySymbol:
            case IEventSymbol:
                EnsureReachable(symbol);
                var qualifier = symbol.IsStatic
                    ? SyntaxFactory.ParseTypeName(symbol.ContainingType.ToDisplayString(DisplayFormats.Type))
                    : _receiver;
                return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, qualifier, current.WithoutTrivia()).WithTriviaFrom(original);

            default:
                return null;
        }
    }

    // The right side of a dot is resolved through its left side and must not be touched.
    private static bool IsNameOfAccess(SimpleNameSyntax node) => node.Parent switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name == node,
        MemberBindingExpressionSyntax => true,
        QualifiedNameSyntax qualified => qualified.Right == node,
        AliasQualifiedNameSyntax => true,
        NameColonSyntax => true,
        NameEqualsSyntax => true,
        _ => false,
    };

    private void EnsureReachable(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal or Accessibility.NotApplicable)
            {
                continue;
            }

            problems.Add($"'{symbol.Name}' is {Describe(current.DeclaredAccessibility)} and the generated check cannot reach it; contract expressions may only use members visible outside the type");
            return;
        }
    }

    private static string Describe(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => accessibility.ToString(),
    };
}
