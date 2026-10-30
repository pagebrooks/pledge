using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PledgeContracts.Roslyn.Benchmarks;

// A project of 50 files with 20 contracted methods each, every one called once, under Enforce. Cold is a first build;
// Incremental is the generator's share of a rebuild after one contract in one file changes, which is what the IDE
// and an incremental build pay on every edit.
[MemoryDiagnoser]
public class GeneratorBenchmarks
{
    private const int Files = 50;
    private const int MethodsPerFile = 20;

    private CSharpCompilation _compilation = null!;
    private CSharpCompilation _edited = null!;
    private GeneratorDriver _driver = null!;
    private GeneratorDriver _warm = null!;

    [GlobalSetup]
    public void Setup()
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "PledgeContracts.Generated")]);
        var trees = Enumerable.Range(0, Files)
            .Select(file => CSharpSyntaxTree.ParseText(Source(file, bound: 0), parseOptions, path: $"/project/File{file}.cs"))
            .ToArray();
        _compilation = CSharpCompilation.Create("Project", trees, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var edit = CSharpSyntaxTree.ParseText(Source(0, bound: 1), parseOptions, path: trees[0].FilePath);
        _edited = _compilation.ReplaceSyntaxTree(trees[0], edit);

        _driver = CSharpGeneratorDriver.Create(
            [new PledgeGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: new OptionsProvider(new Dictionary<string, string>(StringComparer.Ordinal) { ["build_property.PledgeMode"] = "Enforce" }));
        _warm = _driver.RunGenerators(_compilation);

        var generated = _warm.GetRunResult().GeneratedTrees.Length;
        if (generated < Files * MethodsPerFile)
        {
            throw new InvalidOperationException($"Expected a wrapper per method, got {generated} generated files.");
        }
    }

    [Benchmark]
    public GeneratorDriver Cold() => _driver.RunGenerators(_compilation);

    [Benchmark]
    public GeneratorDriver Incremental() => _warm.RunGenerators(_edited);

    private static string Source(int file, int bound)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using PledgeContracts;");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"namespace Project.Area{file};");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"public sealed class Account{file}");
        builder.AppendLine("{");
        builder.AppendLine("    public decimal Balance { get; private set; } = 1_000m;");
        for (var method = 0; method < MethodsPerFile; method++)
        {
            // The edit changes the first contract in the file, so exactly one method's wrapper is regenerated.
            var floor = method == 0 ? bound : 0;
            builder.AppendLine(CultureInfo.InvariantCulture, $$"""

                    public decimal Withdraw{{method}}(decimal amount)
                    {
                        Pledge.Requires(amount > {{floor}});
                        Pledge.Requires(amount <= Balance, "insufficient funds");
                        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
                        Balance -= amount;
                        return Balance;
                    }
                """);
        }

        builder.AppendLine();
        builder.AppendLine("    public decimal Run()");
        builder.AppendLine("    {");
        builder.AppendLine("        var total = 0m;");
        for (var method = 0; method < MethodsPerFile; method++)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"        total += Withdraw{method}(1m);");
        }

        builder.AppendLine("        return total;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static ImmutableArray<MetadataReference> References()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(path => string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.Ordinal))
                .Append(typeof(Pledge).Assembly.Location)
                .Append(typeof(RequiresAttribute).Assembly.Location)
                .Select(path => MetadataReference.CreateFromFile(path)),
        ];
    }

    private sealed class OptionsProvider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public static readonly Options Empty = new(new Dictionary<string, string>());

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
        }
    }
}
