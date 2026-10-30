using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using PledgeContracts.Roslyn.Generator;

namespace PledgeContracts.Roslyn.CodeFixes;

// PLEDGE001: moves a contract statement up to the end of the leading contract run. Offered only when the
// statement is a direct child of the method body and reads no local declared in that body, since the moved
// statement would otherwise not compile.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MoveContractToTopCodeFixProvider))]
[Shared]
public sealed class MoveContractToTopCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.ContractNotAtTop);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not
            {
                Parent: ExpressionStatementSyntax { Parent: BlockSyntax { Parent: MethodDeclarationSyntax } body } statement,
            })
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null || ReadsBodyLocal(statement, body, model))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Move contract to the top of the method",
                cancellationToken => MoveAsync(context.Document, root, body, statement, cancellationToken),
                equivalenceKey: nameof(MoveContractToTopCodeFixProvider)),
            context.Diagnostics);
    }

    private static bool ReadsBodyLocal(StatementSyntax statement, BlockSyntax body, SemanticModel model)
    {
        var flow = model.AnalyzeDataFlow(statement);
        if (flow is null || !flow.Succeeded)
        {
            return true;
        }

        foreach (var symbol in flow.ReadInside)
        {
            if (symbol is ILocalSymbol local && local.DeclaringSyntaxReferences.Any(reference => body.Span.Contains(reference.Span) && !statement.Span.Contains(reference.Span)))
            {
                return true;
            }
        }

        return false;
    }

    private static Task<Document> MoveAsync(Document document, SyntaxNode root, BlockSyntax body, ExpressionStatementSyntax statement, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var remaining = body.Statements.Remove(statement);
        var insertAt = 0;
        while (insertAt < remaining.Count && ContractSyntax.IsContractCall(remaining[insertAt], out _))
        {
            insertAt++;
        }

        var moved = statement.WithAdditionalAnnotations(Formatter.Annotation);
        var newBody = body.WithStatements(remaining.Insert(insertAt, moved));
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(body, newBody)));
    }
}
