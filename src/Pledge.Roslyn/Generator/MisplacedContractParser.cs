using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Pledge calls compile away wherever they are, so one placed where the generator cannot act on it would vanish
// silently. This names those places. Calls inside an ordinary block-bodied method are left to the method pipeline
// and the placement analyzer. The place is checked from syntax before the call is bound: the generator re-runs this
// for every Pledge call in the project on each edit, and nearly all of them sit in ordinary methods.
internal static class MisplacedContractParser
{
    public static MisplacedContract? Parse(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            var problem = ancestor switch
            {
                AnonymousFunctionExpressionSyntax => "contracts inside lambdas are not supported yet",
                LocalFunctionStatementSyntax => "contracts on local functions are not supported yet",
                AccessorDeclarationSyntax => "property accessors cannot be intercepted; contracts on properties come with decorator mode",
                PropertyDeclarationSyntax or IndexerDeclarationSyntax => "property accessors cannot be intercepted; contracts on properties come with decorator mode",
                ConstructorDeclarationSyntax => "contracts on constructors are not supported yet",
                OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => "contracts on operators are not supported yet",
                GlobalStatementSyntax => "contracts in top-level statements have no method to intercept",
                MethodDeclarationSyntax { ExpressionBody: not null } => "expression-bodied methods cannot carry contracts; use a block body",
                MethodDeclarationSyntax => null,
                _ => _continue,
            };

            if (ReferenceEquals(problem, _continue))
            {
                continue;
            }

            if (problem is null
                || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
                || method.ContainingType.ToDisplayString() != ContractParser.PledgeTypeName)
            {
                return null;
            }

            var member = ancestor.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault() is { } declaration
                && model.GetDeclaredSymbol(declaration, cancellationToken) is { } symbol
                ? symbol.Name
                : method.Name;

            return new MisplacedContract(SourceSite.Of(invocation), member, problem);
        }

        return null;
    }

    private static readonly string _continue = new('\0', 1);
}
