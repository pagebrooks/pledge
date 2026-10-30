using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace PledgeContracts.Roslyn.CodeFixes;

// PLEDGE007: copies the snapshotted value so Old(...) holds contents, not a live reference. Sequences get
// .ToArray(), records get a with expression; the analyzer says which applies in the diagnostic's properties.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CopyOldValueCodeFixProvider))]
[Shared]
public sealed class CopyOldValueCodeFixProvider : CodeFixProvider
{
    // Mirror ContractAnalyzer's property name and values; the two assemblies share no types.
    private const string CopyProperty = "Copy";
    private const string CopyWithToArray = "ToArray";
    private const string CopyWithWith = "With";
    private const string LinqNamespace = "System.Linq";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.OldOfMutableReference);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue(CopyProperty, out var copy) || copy is not (CopyWithToArray or CopyWithWith))
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { ArgumentList.Arguments: { Count: 1 } arguments })
        {
            return;
        }

        var argument = arguments[0].Expression;
        context.RegisterCodeFix(
            CodeAction.Create(
                copy == CopyWithToArray ? "Snapshot a copy with .ToArray()" : "Snapshot a copy with a with expression",
                cancellationToken => CopyAsync(context.Document, root, argument, copy, cancellationToken),
                equivalenceKey: nameof(CopyOldValueCodeFixProvider) + copy),
            diagnostic);
    }

    private static async Task<Document> CopyAsync(Document document, SyntaxNode root, ExpressionSyntax argument, string copy, CancellationToken cancellationToken)
    {
        var operand = argument is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or ThisExpressionSyntax
            ? argument.WithoutTrivia()
            : SyntaxFactory.ParenthesizedExpression(argument.WithoutTrivia());

        ExpressionSyntax replacement = copy == CopyWithToArray
            ? SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, operand, SyntaxFactory.IdentifierName("ToArray")))
            : SyntaxFactory.WithExpression(operand, SyntaxFactory.InitializerExpression(SyntaxKind.WithInitializerExpression));

        var newRoot = root.ReplaceNode(argument, replacement.WithTriviaFrom(argument));
        if (copy == CopyWithToArray && newRoot is CompilationUnitSyntax unit && !await LinqIsImportedAsync(document, argument, cancellationToken).ConfigureAwait(false))
        {
            newRoot = unit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(LinqNamespace)).WithAdditionalAnnotations(Formatter.Annotation));
        }

        return document.WithSyntaxRoot(newRoot);
    }

    // ToArray is an extension method, so the test is whether Enumerable's extensions are in scope at the call.
    private static async Task<bool> LinqIsImportedAsync(Document document, ExpressionSyntax argument, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model?.GetTypeInfo(argument, cancellationToken).Type is not { } type)
        {
            return true;
        }

        return model.LookupSymbols(argument.SpanStart, type, "ToArray", includeReducedExtensionMethods: true).Length > 0;
    }
}
