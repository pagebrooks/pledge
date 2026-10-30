using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Cheap syntactic tests that run before any binding. They over-approximate; the parsers confirm with symbols.
internal static class ContractSyntax
{
    public static bool IsContractName(string? name) => name is "Requires" or "Ensures" or "EnsuresOnThrow";

    public static bool IsContractInvocation(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation && IsContractName(NameOf(invocation.Expression));

    // A call written Pledge.Requires(...), Pledge.Old(...) and so on. These are never call sites of a contracted
    // method, and a contracted method carries several of them, so the call-site pipeline skips them before binding.
    public static bool IsPledgeCall(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name, Expression: var target }
        && name is "Requires" or "Ensures" or "EnsuresOnThrow" or "Old" or "Result"
        && target is IdentifierNameSyntax { Identifier.ValueText: "Pledge" } or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Pledge" };

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

    public static string? NameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
        _ => null,
    };
}
