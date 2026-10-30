using Microsoft.CodeAnalysis;
using PledgeContracts.Roslyn.CodeFixes;

namespace PledgeContracts.Roslyn.Tests;

public sealed class PlacementAnalyzerTests
{
    [Fact]
    public async Task Contracts_at_the_top_of_a_block_body_are_fine()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync("""
            using PledgeContracts;

            public sealed class Holder
            {
                public int Value { get; private set; }

                public int M(int x)
                {
                    Pledge.Requires(x > 0);
                    Pledge.Ensures(Pledge.Result<int>() > 0);
                    Pledge.EnsuresOnThrow<System.Exception>(Value == Pledge.Old(Value));
                    Value = x;
                    return x;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("var limit = 10; Pledge.Requires(x > 0);", "Requires")]
    [InlineData("if (x > 0) { Pledge.Ensures(Pledge.Result<int>() > 0); }", "Ensures")]
    [InlineData("Pledge.Requires(x > 0); System.Console.WriteLine(x); Pledge.EnsuresOnThrow<System.Exception>(x > 0);", "EnsuresOnThrow")]
    [InlineData("Other.Requires(x > 0); Pledge.Requires(x > 0);", "Requires")]
    public async Task A_contract_after_another_statement_is_PLEDGE001(string body, string expectedName)
    {
        var diagnostic = await AnalyzerHarness.SingleAsync($$"""
            using PledgeContracts;

            public static class Other { public static void Requires(bool c) { } }

            public sealed class Holder
            {
                public int M(int x)
                {
                    {{body}}
                    return x;
                }
            }
            """, "PLEDGE001");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"Pledge.{expectedName} is a contract only at the top of the method body", diagnostic.Message(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contracts_in_places_the_generator_reports_are_not_PLEDGE001()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync("""
            using PledgeContracts;

            public sealed class Holder
            {
                public Holder(int seed) { var x = seed; Pledge.Requires(seed > 0); }

                public int Value { get { var x = 1; Pledge.Ensures(Pledge.Result<int>() > 0); return x; } }

                public void M() { System.Action a = () => { var y = 1; Pledge.Requires(y > 0); }; a(); }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_fix_moves_the_contract_after_the_leading_contracts()
    {
        var fixedSource = await AnalyzerHarness.ApplyFixToSingleFileAsync("""
            using PledgeContracts;

            public sealed class Holder
            {
                public int M(int x)
                {
                    Pledge.Requires(x > 0);
                    System.Console.WriteLine(x);
                    Pledge.Requires(x < 100);
                    return x;
                }
            }
            """, new MoveContractToTopCodeFixProvider(), "PLEDGE001");

        Assert.Equal("""
            using PledgeContracts;

            public sealed class Holder
            {
                public int M(int x)
                {
                    Pledge.Requires(x > 0);
                    Pledge.Requires(x < 100);
                    System.Console.WriteLine(x);
                    return x;
                }
            }
            """, fixedSource);
    }

    [Fact]
    public async Task No_fix_when_the_contract_reads_a_local_declared_above_it()
    {
        var offered = await AnalyzerHarness.OffersFixAsync("""
            using PledgeContracts;

            public sealed class Holder
            {
                public int M(int x)
                {
                    var limit = 10;
                    Pledge.Requires(x < limit);
                    return x;
                }
            }
            """, new MoveContractToTopCodeFixProvider(), "PLEDGE001");

        Assert.False(offered);
    }
}
