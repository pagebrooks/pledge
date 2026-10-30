using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace PledgeContracts.Roslyn.CodeFixes;

// PLEDGE004, for a call to a method declared in the solution: adds [Pure] to that method. The author is asserting
// the method has no side effects; the analyzer takes their word for it from then on.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MarkPureCodeFixProvider))]
[Shared]
public sealed class MarkPureCodeFixProvider : CodeFixProvider
{
    private const string PureAttributeName = "global::PledgeContracts.Pure";
    private const string PureNamespace = "PledgeContracts";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.ImpureContract);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocation)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        method = (method.ReducedFrom ?? method).OriginalDefinition;
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) || method.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken);
        if (declaration is not (MethodDeclarationSyntax or LocalFunctionStatementSyntax)
            || context.Document.Project.Solution.GetDocument(declaration.SyntaxTree) is not { } target)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Mark '{method.Name}' as [Pure]",
                cancellationToken => MarkAsync(target, declaration, cancellationToken),
                equivalenceKey: nameof(MarkPureCodeFixProvider) + method.Name),
            context.Diagnostics);
    }

    private static async Task<Solution> MarkAsync(Document document, SyntaxNode declaration, CancellationToken cancellationToken)
    {
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        // The declaration's leading trivia (doc comment, indentation) moves onto the attribute, which now comes first.
        var attribute = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Attribute(SyntaxFactory.ParseName(PureAttributeName))))
            .WithLeadingTrivia(declaration.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Simplifier.Annotation, Formatter.Annotation);

        SyntaxNode marked = declaration.WithLeadingTrivia(SyntaxFactory.ElasticMarker) switch
        {
            MethodDeclarationSyntax method => method.AddAttributeLists(attribute),
            LocalFunctionStatementSyntax local => local.AddAttributeLists(attribute),
            var other => other,
        };

        var newRoot = root.ReplaceNode(declaration, marked);
        if (newRoot is CompilationUnitSyntax unit && !unit.Usings.Any(directive => directive.Name?.ToString() == PureNamespace) && !ImportsNamespace(await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)))
        {
            newRoot = unit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(PureNamespace)).WithAdditionalAnnotations(Formatter.Annotation));
        }

        return document.Project.Solution.WithDocumentSyntaxRoot(document.Id, newRoot);
    }

    // A global using somewhere in the project already imports PledgeContracts; adding a second using would be noise.
    private static bool ImportsNamespace(Compilation? compilation)
    {
        if (compilation is null)
        {
            return false;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.GetRoot() is CompilationUnitSyntax unit && unit.Usings.Any(directive => directive.GlobalKeyword != default && directive.Name?.ToString() == PureNamespace))
            {
                return true;
            }
        }

        return false;
    }
}
