using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Produces the contract text shown in messages and metadata: the author's expression with Pledge.Old(x) as Old(x)
// and Pledge.Result<T>() as Result, whitespace normalized.
internal sealed class ContractTextNormalizer(SemanticModel model, INamedTypeSymbol pledgeType) : CSharpSyntaxRewriter
{
    public string Normalize(ExpressionSyntax expression) => Visit(expression)!.NormalizeWhitespace().ToString();

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (model.GetSymbolInfo(node).Symbol is IMethodSymbol method && SymbolEqualityComparer.Default.Equals(method.ContainingType, pledgeType))
        {
            switch (method.Name)
            {
                case "Result":
                    return SyntaxFactory.IdentifierName("Result").WithTriviaFrom(node);
                case "Old":
                    var arguments = (ArgumentListSyntax)Visit(node.ArgumentList)!;
                    return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Old"), arguments).WithTriviaFrom(node);
            }
        }

        return base.VisitInvocationExpression(node);
    }
}
