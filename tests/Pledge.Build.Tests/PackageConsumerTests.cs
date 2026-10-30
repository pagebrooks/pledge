using System.IO.Compression;
using System.Reflection;

namespace PledgeContracts.Build.Tests;

// Packs Pledge and Pledge.Attributes from this run's build output and consumes them the way a user does: a
// PackageReference restored from a local feed. The other build tests wire the assemblies and targets in by hand,
// so only these catch a packaging mistake, such as targets that never reach a consumer.
public sealed class PackageConsumerTests
{
    private const string Version = "0.0.0-consumer";

    private static readonly Lazy<Task<string>> _feed = new(PackAsync);

    [Fact]
    public async Task The_package_carries_the_generator_the_code_fixes_and_transitive_targets()
    {
        var entries = await EntriesAsync("Pledge");

        Assert.Contains("README.md", entries);
        Assert.Contains("lib/net10.0/Pledge.dll", entries);
        Assert.Contains("analyzers/dotnet/cs/Pledge.Roslyn.dll", entries);
        Assert.Contains("analyzers/dotnet/cs/Pledge.Roslyn.CodeFixes.dll", entries);
        Assert.Contains("buildTransitive/Pledge.props", entries);
        Assert.Contains("buildTransitive/Pledge.targets", entries);
        Assert.DoesNotContain(entries, entry => entry.StartsWith("build/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_attributes_package_targets_netstandard2_0_and_has_a_readme()
    {
        var entries = await EntriesAsync("Pledge.Attributes");

        Assert.Contains("README.md", entries);
        Assert.Contains("lib/netstandard2.0/Pledge.Attributes.dll", entries);
    }

    [Fact]
    public async Task A_package_reference_checks_contracts_and_runs_the_analyzers()
    {
        using var fixture = Fixture.Create();
        fixture.UseFeed(await _feed.Value);
        fixture.WriteProject("App", withPledge: false, outputType: "Exe", items: [$"<PackageReference Include=\"Pledge\" Version=\"{Version}\" />"]);
        fixture.CopySources("App", "Modes");
        fixture.WriteSource("App", "Constant.cs", """
            namespace Fixture;

            internal static class Constant
            {
                public static decimal Overdraw(Account account) => account.Withdraw(-1m);
            }
            """);

        var build = await fixture.BuildAsync("App", "Debug");

        build.AssertSucceeded();
        Assert.Contains("warning PLEDGE008", build.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("PLEDGE005", build.Output, StringComparison.Ordinal);
        AssertEnforced(await fixture.DotnetAsync(fixture.OutputPath("App", "Debug")));
    }

    // NuGet keeps a package's build folder out of projects that only reference a project using the package, but
    // its analyzers still reach them; without buildTransitive the generator ran there with no InterceptorsNamespaces.
    [Fact]
    public async Task A_project_that_gets_Pledge_through_a_project_reference_checks_its_own_contracts()
    {
        using var fixture = Fixture.Create();
        fixture.UseFeed(await _feed.Value);
        fixture.WriteProject(
            "Lib",
            withPledge: false,
            properties: new Dictionary<string, string> { ["AssemblyName"] = "Lib" },
            items: [$"<PackageReference Include=\"Pledge\" Version=\"{Version}\" />"]);
        fixture.WriteProject("App", withPledge: false, outputType: "Exe", items: ["<ProjectReference Include=\"../Lib/Lib.csproj\" />"]);
        fixture.CopySources("App", "Modes");

        var build = await fixture.BuildAsync("App", "Debug");

        build.AssertSucceeded();
        Assert.DoesNotContain("PLEDGE005", build.Output, StringComparison.Ordinal);
        AssertEnforced(await fixture.DotnetAsync(fixture.OutputPath("App", "Debug")));
    }

    private static async Task<HashSet<string>> EntriesAsync(string id)
    {
        using var package = ZipFile.OpenRead(Path.Combine(await _feed.Value, $"{id}.{Version}.nupkg"));
        return package.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
    }

    private static void AssertEnforced(ProcessResult run)
    {
        run.AssertSucceeded();
        Assert.Contains("deposit: threw PreconditionViolationException", run.Output, StringComparison.Ordinal);
        Assert.Contains("withdraw: threw PostconditionViolationException", run.Output, StringComparison.Ordinal);
    }

    // --no-build packs what the test run already built, so the package holds the same assemblies the other tests use.
    private static async Task<string> PackAsync()
    {
        var configuration = typeof(PackageConsumerTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "Configuration").Value!;
        var feed = Path.Combine(Path.GetTempPath(), "pledge-build-tests", "feed-" + Guid.NewGuid().ToString("N"));

        foreach (var project in new[] { "Pledge.Attributes", "Pledge" })
        {
            var result = await ProcessRunner.RunAsync(BuildHarness.Dotnet, BuildHarness.RepositoryRoot, [
                "pack", Path.Combine("src", project, project + ".csproj"),
                "--configuration", configuration,
                "--no-build",
                "--output", feed,
                "-nologo",
                "-p:MinVerVersionOverride=" + Version,
            ]);
            result.AssertSucceeded();
        }

        return feed;
    }
}
