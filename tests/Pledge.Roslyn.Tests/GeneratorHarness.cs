using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PledgeContracts.Roslyn.Tests;

// Runs the generator over snippets the way the compiler would: real BCL and Pledge references, the interceptors
// feature flag, the PledgeMode global option. The result can be compiled and executed so tests assert behaviour,
// not only generated text.
internal static class GeneratorHarness
{
    public const string SourcePath = "/project/Source0.cs";

    private static readonly Lazy<ImmutableArray<MetadataReference>> _references = new(LoadReferences);
    private static int _assemblyCounter;

    public static GeneratorRun Run(string source, string mode = "Enforce", bool interceptorsEnabled = true, params string[] extraSources)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        if (interceptorsEnabled)
        {
            parseOptions = parseOptions.WithFeatures([new KeyValuePair<string, string>(PledgeGenerator.InterceptorsFeature, "PledgeContracts.Generated")]);
        }

        var trees = new[] { source }.Concat(extraSources)
            .Select((text, index) => CSharpSyntaxTree.ParseText(text, parseOptions, path: $"/project/Source{index}.cs"))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "PledgeSnippet" + Interlocked.Increment(ref _assemblyCounter),
            trees,
            _references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PledgeGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: new OptionsProvider(mode),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        return GeneratorRun.Create(driver, compilation);
    }

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.Ordinal));

        return trusted
            .Append(typeof(Pledge).Assembly.Location)
            .Append(typeof(RequiresAttribute).Assembly.Location)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private sealed class OptionsProvider(string? mode) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(mode);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options(string? mode) : AnalyzerConfigOptions
        {
            public static readonly Options Empty = new(null);

            public override bool TryGetValue(string key, out string value)
            {
                if (mode is not null && key == "build_property.PledgeMode")
                {
                    value = mode;
                    return true;
                }

                value = null!;
                return false;
            }
        }
    }
}

internal sealed class GeneratorRun
{
    private GeneratorRun(GeneratorDriver driver, Compilation input, Compilation output, ImmutableArray<Diagnostic> diagnostics)
    {
        Driver = driver;
        InputCompilation = input;
        OutputCompilation = output;
        GeneratorDiagnostics = diagnostics;
    }

    public GeneratorDriver Driver { get; }

    public Compilation InputCompilation { get; }

    public Compilation OutputCompilation { get; }

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

    public GeneratorDriverRunResult Result => Driver.GetRunResult();

    public IReadOnlyDictionary<string, string> GeneratedSources =>
        Result.GeneratedTrees.ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);

    public string SingleGeneratedSource => Assert.Single(GeneratedSources).Value;

    public static GeneratorRun Create(GeneratorDriver driver, Compilation compilation)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new GeneratorRun(driver, compilation, output, diagnostics);
    }

    // Re-runs the same driver over a compilation with one more file, to observe what the pipeline cached.
    public GeneratorRun Rerun(string extraSource)
    {
        var tree = CSharpSyntaxTree.ParseText(extraSource, (CSharpParseOptions)InputCompilation.SyntaxTrees.First().Options, path: "/project/Unrelated.cs");
        return Create(Driver, InputCompilation.AddSyntaxTrees(tree));
    }

    public void AssertCompiles()
    {
        var errors = OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    // Emits the compilation and calls a public static method on it, unwrapping reflection's exception wrapper.
    public object? Invoke(string typeName, string methodName)
    {
        AssertCompiles();

        using var stream = new MemoryStream();
        var emitted = OutputCompilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(d => d.ToString())));
        stream.Position = 0;

        var assembly = new AssemblyLoadContext(OutputCompilation.AssemblyName).LoadFromStream(stream);
        var type = assembly.GetType(typeName) ?? throw new InvalidOperationException($"Type {typeName} not found in the snippet.");
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException($"Method {methodName} not found.");

        try
        {
            return method.Invoke(null, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
