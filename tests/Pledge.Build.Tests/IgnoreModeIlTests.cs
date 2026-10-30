namespace PledgeContracts.Build.Tests;

// DESIGN.md promises that Ignore produces the assembly a project without Pledge would. Each test builds the
// Ledger fixture twice with the real SDK: once as written, referencing Pledge in Ignore mode, and once with every
// Pledge line and the PledgeContracts using deleted and no reference to Pledge at all.
public sealed class IgnoreModeIlTests
{
    [Theory]
    [InlineData("Release")]
    [InlineData("Debug")]
    public async Task Without_PDBs_the_assembly_is_byte_identical_to_one_built_without_Pledge(string configuration)
    {
        using var fixture = Fixture.Create();
        var (with, without) = await BuildPairAsync(fixture, configuration, debugType: "none", metadata: false);

        AssertSame(File.ReadAllBytes(without), File.ReadAllBytes(with), without, with);
    }

    [Fact]
    public async Task With_PDBs_only_the_identity_derived_from_the_PDB_differs()
    {
        using var fixture = Fixture.Create();
        var (with, without) = await BuildPairAsync(fixture, "Release", debugType: "portable", metadata: false);

        Assert.NotEqual(File.ReadAllBytes(without), File.ReadAllBytes(with));
        AssertSame(Assemblies.WithoutBuildIdentity(without), Assemblies.WithoutBuildIdentity(with), without, with);
    }

    [Fact]
    public async Task With_metadata_on_only_the_assembly_attributes_and_their_reference_are_added()
    {
        using var fixture = Fixture.Create();
        var (with, without) = await BuildPairAsync(fixture, "Release", debugType: "none", metadata: true);

        var expected = Assemblies.Disassemble(without, includeAssemblyHeader: false);
        var actual = Assemblies.Disassemble(with, includeAssemblyHeader: false);
        Assert.True(expected == actual, Assemblies.FirstDifference(expected, actual));

        var header = Assemblies.Disassemble(with);
        Assert.Contains(".assembly extern Pledge.Attributes", header, StringComparison.Ordinal);
        Assert.Contains("PledgeContracts.RequiresAttribute", header, StringComparison.Ordinal);
        Assert.DoesNotContain(".assembly extern Pledge\n", header.Replace("\r", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static async Task<(string With, string Without)> BuildPairAsync(Fixture fixture, string configuration, string debugType, bool metadata)
    {
        var source = File.ReadAllText(BuildHarness.FixtureSource("Ledger", "Ledger.cs"));

        fixture.WriteProject("With", withPledge: true, properties: new Dictionary<string, string>
        {
            ["PledgeMode"] = "Ignore",
            ["PledgeMetadata"] = metadata ? "true" : "false",
            ["DebugType"] = debugType,
        });
        fixture.WriteSource("With", "Ledger.cs", source);

        fixture.WriteProject("Without", withPledge: false, properties: new Dictionary<string, string> { ["DebugType"] = debugType });
        fixture.WriteSource("Without", "Ledger.cs", Strip(source));

        var builds = await Task.WhenAll(fixture.BuildAsync("With", configuration), fixture.BuildAsync("Without", configuration));
        builds[0].AssertSucceeded();
        builds[1].AssertSucceeded();

        return (fixture.OutputPath("With", configuration), fixture.OutputPath("Without", configuration));
    }

    // Deletes the contract statements (each is one line in the fixture) and the using that brings Pledge in.
    private static string Strip(string source)
    {
        var kept = source.Split('\n').Where(line =>
        {
            var trimmed = line.Trim();
            return !trimmed.StartsWith("Pledge.", StringComparison.Ordinal) && trimmed != "using PledgeContracts;";
        });

        var stripped = string.Join('\n', kept);
        Assert.DoesNotContain("Pledge.", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("PledgeContracts", stripped, StringComparison.Ordinal);
        return stripped;
    }

    private static void AssertSame(byte[] expected, byte[] actual, string expectedPath, string actualPath)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            Assert.Fail(Assemblies.DescribeByteDifferences(expected, actual, expectedPath) + Environment.NewLine
                + Assemblies.FirstDifference(Assemblies.Disassemble(expectedPath), Assemblies.Disassemble(actualPath)));
        }
    }
}
