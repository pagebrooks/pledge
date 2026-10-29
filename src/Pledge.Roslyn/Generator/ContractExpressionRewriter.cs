using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Rewrites a contract expression so it means the same thing inside the generated wrapper, where the receiver is a
// parameter named @this and none of the author's usings apply: implicit instance members become @this.Member,
// static members and types are spelled out with global::, parameters and lambda locals stay as they are.
// Members the wrapper cannot reach (private, protected) are recorded as problems instead of generating broken code.
internal sealed class ContractExpressionRewriter(SemanticModel model, List<string> problems) : CSharpSyntaxRewriter
{
    private static readonly IdentifierNameSyntax _receiver = SyntaxFactory.IdentifierName("@this");

    public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node) => _receiver.WithTriviaFrom(node);

    public override SyntaxNode? VisitBaseExpression(BaseExpressionSyntax node)
    {
        problems.Add("'base' cannot be used in a contract expression");
        return node;
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => Qualify(node, node) ?? node;

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
    {
        var visited = (GenericNameSyntax)base.VisitGenericName(node)!;
        return Qualify(node, visited) ?? visited;
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
