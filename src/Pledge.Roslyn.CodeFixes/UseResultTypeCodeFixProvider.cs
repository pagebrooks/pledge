using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace PledgeContracts.Roslyn.CodeFixes;

// PLEDGE002, type mismatch only: replaces the type argument of Result<T>() with the method's result type, which
// the analyzer puts in the diagnostic's properties fully qualified.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseResultTypeCodeFixProvider))]
[Shared]
public sealed class UseResultTypeCodeFixProvider : CodeFixProvider
{
    // Mirrors ContractAnalyzer.ResultTypeProperty; the two assemblies share no types.
    private const string ResultTypeProperty = "ResultType";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.ResultMisused);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue(ResultTypeProperty, out var resultType) || resultType is null)
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var invocation = root?.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        var name = invocation?.Expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => generic,
            GenericNameSyntax generic => generic,
            _ => null,
        };

        if (root is null || name is null)
        {
            return;
        }

        var display = resultType.StartsWith("global::", StringComparison.Ordinal) ? resultType.Substring("global::".Length) : resultType;
        context.RegisterCodeFix(
            CodeAction.Create(
                $"Use Result<{display}>()",
                cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var typeArguments = SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.ParseTypeName(resultType).WithAdditionalAnnotations(Simplifier.Annotation)));
                    return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(name, name.WithTypeArgumentList(typeArguments))));
                },
                equivalenceKey: nameof(UseResultTypeCodeFixProvider)),
            diagnostic);
    }
}
