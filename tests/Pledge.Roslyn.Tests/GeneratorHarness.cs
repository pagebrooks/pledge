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

    public static ImmutableArray<MetadataReference> References => _references.Value;

    public static CSharpCompilationOptions CompilationOptions { get; } = new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    public static CSharpParseOptions ParseOptions(bool interceptorsEnabled = true)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        return interceptorsEnabled
            ? parseOptions.WithFeatures([new KeyValuePair<string, string>(PledgeGenerator.InterceptorsFeature, "PledgeContracts.Generated")])
            : parseOptions;
    }

    public static CSharpCompilation CreateCompilation(IEnumerable<string> sources, CSharpParseOptions parseOptions)
    {
        var trees = sources
            .Select((text, index) => CSharpSyntaxTree.ParseText(text, parseOptions, path: $"/project/Source{index}.cs"))
            .ToArray();

        return CSharpCompilation.Create("PledgeSnippet" + Interlocked.Increment(ref _assemblyCounter), trees, _references.Value, CompilationOptions);
    }

    public static GeneratorRun Run(
        string source,
        string mode = "Enforce",
        bool interceptorsEnabled = true,
        string? metadata = null,
        string? preconditions = null,
        string? postconditions = null,
        string? registry = null,
        params string[] extraSources)
    {
        var parseOptions = ParseOptions(interceptorsEnabled);
        var compilation = CreateCompilation(new[] { source }.Concat(extraSources), parseOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PledgeGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: new OptionsProvider(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["build_property.PledgeMode"] = mode,
                ["build_property.PledgeMetadata"] = metadata,
                ["build_property.PledgePreconditions"] = preconditions,
                ["build_property.PledgePostconditions"] = postconditions,
                ["build_property.PledgeRegistry"] = registry,
            }),
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

    // Global options the way Pledge.targets hands them over; a null value is a property that was never set.
    private sealed class OptionsProvider(IReadOnlyDictionary<string, string?> globals) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globals);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options(IReadOnlyDictionary<string, string?> values) : AnalyzerConfigOptions
        {
            public static readonly Options Empty = new(new Dictionary<string, string?>());

            public override bool TryGetValue(string key, out string value)
            {
                var found = values.TryGetValue(key, out var candidate) ? candidate : null;
                value = found!;
                return found is not null;
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

    // The wrapper files, leaving the metadata file and the testing probes aside.
    public IReadOnlyDictionary<string, string> Wrappers =>
        GeneratedSources.Where(pair => pair.Key != "PledgeContracts.Metadata.g.cs" && !IsProbe(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Probes =>
        GeneratedSources.Where(pair => IsProbe(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public string? Metadata => GeneratedSources.TryGetValue("PledgeContracts.Metadata.g.cs", out var source) ? source : null;

    public string SingleWrapper => Assert.Single(Wrappers).Value;

    // Emits the compilation and loads it, so tests can reflect over what the generator put into the assembly.
    public System.Reflection.Assembly Load()
    {
        AssertCompiles();

        using var stream = new MemoryStream();
        var emitted = OutputCompilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(d => d.ToString())));
        stream.Position = 0;
        return new AssemblyLoadContext(OutputCompilation.AssemblyName).LoadFromStream(stream);
    }

    private static bool IsProbe(string hintName) => hintName.EndsWith(".Probe.g.cs", StringComparison.Ordinal);

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
        var assembly = Load();
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
