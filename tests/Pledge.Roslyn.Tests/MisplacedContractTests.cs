namespace PledgeContracts.Roslyn.Tests;

public sealed class MisplacedContractTests
{
    [Theory]
    [InlineData("public int Value { get { Pledge.Ensures(Pledge.Result<int>() >= 0); return 1; } }", "property accessors cannot be intercepted")]
    [InlineData("public int Value { get => 1; set { Pledge.Requires(value >= 0); } }", "property accessors cannot be intercepted")]
    [InlineData("public Holder(int seed) { Pledge.Requires(seed > 0); }", "constructors are not supported yet")]
    [InlineData("public void M() => Pledge.Requires(true);", "expression-bodied methods cannot carry contracts")]
    [InlineData("public void M() { System.Action a = () => Pledge.Requires(true); a(); }", "inside lambdas")]
    [InlineData("public void M() { Local(); void Local() { Pledge.Requires(true); } }", "local functions")]
    [InlineData("public static Holder operator +(Holder a, Holder b) { Pledge.Requires(a != null); return a; }", "operators")]
    public void Contracts_the_generator_cannot_reach_report_PLEDGE009(string member, string expectedProblem)
    {
        var run = GeneratorHarness.Run($$"""
            using PledgeContracts;

            public sealed class Holder
            {
                {{member}}
            }
            """);

        run.AssertCompiles();
        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("PLEDGE009", diagnostic.Id);
        Assert.Contains(expectedProblem, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedSources);
    }

    [Fact]
    public void A_contract_at_the_top_of_a_block_bodied_method_is_not_misplaced()
    {
        var run = GeneratorHarness.Run("""
            using PledgeContracts;

            public sealed class Holder
            {
                public int M(int x)
                {
                    Pledge.Requires(x > 0);
                    return x;
                }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
    }
}
