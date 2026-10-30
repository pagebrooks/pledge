using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using PledgeContracts.Roslyn.Analyzers;

namespace PledgeContracts.Roslyn.Tests;

// Runs the PLEDGE analyzers over a snippet with the same references the generator harness uses, and applies code
// fixes through an AdhocWorkspace so the result goes through Roslyn's usual simplification and formatting.
internal static class AnalyzerHarness
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers = [new ContractAnalyzer(), new ConstantArgumentAnalyzer(), new UncoveredCallAnalyzer()];

    public static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, bool allowCompileErrors = false, params string[] extraSources) =>
        AnalyzeAsync(source, globalOptions: null, allowCompileErrors, extraSources);

    // Global options are the build_property.* values MSBuild would hand the analyzers, such as the modes.
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, IReadOnlyDictionary<string, string?>? globalOptions, bool allowCompileErrors = false, params string[] extraSources)
    {
        var compilation = GeneratorHarness.CreateCompilation(new[] { source }.Concat(extraSources), GeneratorHarness.ParseOptions());
        if (!allowCompileErrors)
        {
            var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        }

        var options = new AnalyzerOptions([], new GeneratorHarness.OptionsProvider(globalOptions ?? new Dictionary<string, string?>()));
        var diagnostics = await compilation.WithAnalyzers(_analyzers, options).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start)];
    }

    public static async Task<Diagnostic> SingleAsync(string source, string id)
    {
        var diagnostics = await AnalyzeAsync(source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(id, diagnostic.Id);
        return diagnostic;
    }

    // Applies the first registered fix for the first diagnostic with the given id and returns the changed document,
    // or the changed documents when the fix touches more than one.
    public static async Task<IReadOnlyDictionary<string, string>> ApplyFixAsync(string source, CodeFixProvider provider, string diagnosticId, params string[] extraSources)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Snippet", LanguageNames.CSharp)
            .WithCompilationOptions(GeneratorHarness.CompilationOptions)
            .WithParseOptions(GeneratorHarness.ParseOptions())
            .WithMetadataReferences(GeneratorHarness.References);

        var sources = new[] { source }.Concat(extraSources).ToArray();
        var documentIds = new List<DocumentId>();
        for (var index = 0; index < sources.Length; index++)
        {
            var document = project.AddDocument($"Source{index}.cs", sources[index]);
            documentIds.Add(document.Id);
            project = document.Project;
        }

        var compilation = (await project.GetCompilationAsync(cancellationToken))!;
        var diagnostics = await compilation.WithAnalyzers(_analyzers).GetAnalyzerDiagnosticsAsync(cancellationToken);
        var diagnostic = diagnostics.Where(d => d.Id == diagnosticId).OrderBy(d => d.Location.SourceSpan.Start).First();
        var target = project.Solution.GetDocument(diagnostic.Location.SourceTree)!;

        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(target, diagnostic, (action, _) => actions.Add(action), cancellationToken));
        Assert.True(actions.Count > 0, $"{provider.GetType().Name} registered no fix for {diagnosticId}.");

        var operations = await actions[0].GetOperationsAsync(cancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in documentIds)
        {
            var document = changed.GetDocument(id)!;
            result[document.Name] = (await document.GetTextAsync(cancellationToken)).ToString();
        }

        return result;
    }

    public static async Task<string> ApplyFixToSingleFileAsync(string source, CodeFixProvider provider, string diagnosticId)
    {
        var result = await ApplyFixAsync(source, provider, diagnosticId);
        return result["Source0.cs"];
    }

    public static async Task<bool> OffersFixAsync(string source, CodeFixProvider provider, string diagnosticId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = new AdhocWorkspace();
        var document = workspace.AddProject("Snippet", LanguageNames.CSharp)
            .WithCompilationOptions(GeneratorHarness.CompilationOptions)
            .WithParseOptions(GeneratorHarness.ParseOptions())
            .WithMetadataReferences(GeneratorHarness.References)
            .AddDocument("Source0.cs", source);

        var compilation = (await document.Project.GetCompilationAsync(cancellationToken))!;
        var diagnostics = await compilation.WithAnalyzers(_analyzers).GetAnalyzerDiagnosticsAsync(cancellationToken);
        var diagnostic = diagnostics.Where(d => d.Id == diagnosticId).OrderBy(d => d.Location.SourceSpan.Start).First();

        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken));
        return actions.Count > 0;
    }

    public static string Message(this Diagnostic diagnostic) => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    public static int Line(this Diagnostic diagnostic) => diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;

    public static string SourceText(this Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan);
}
