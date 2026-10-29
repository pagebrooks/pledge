using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Cheap syntactic tests that run before any binding. They over-approximate; the parsers confirm with symbols.
internal static class ContractSyntax
{
    public static bool IsContractName(string? name) => name is "Requires" or "Ensures" or "EnsuresOnThrow";

    public static bool IsCandidateDeclaration(SyntaxNode node) =>
        node is MethodDeclarationSyntax { Body.Statements: { Count: > 0 } statements } && IsContractCall(statements[0], out _);

    public static bool IsContractCall(StatementSyntax statement, out InvocationExpressionSyntax invocation)
    {
        if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax candidate } && IsContractName(NameOf(candidate.Expression)))
        {
            invocation = candidate;
            return true;
        }

        invocation = null!;
        return false;
    }

    private static string? NameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
        _ => null,
    };
}
