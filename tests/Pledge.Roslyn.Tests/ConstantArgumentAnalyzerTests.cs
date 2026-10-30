using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn.Tests;

public sealed class ConstantArgumentAnalyzerTests
{
    private const string Account = """
        using PledgeContracts;

        public sealed class Account
        {
            public decimal Balance { get; private set; } = 100m;

            public decimal Withdraw(decimal amount)
            {
                Pledge.Requires(amount > 0);
                Pledge.Requires(amount <= Balance, "insufficient funds");
                Balance -= amount;
                return Balance;
            }

            public static int Clamp(int value, int min, int max)
            {
                Pledge.Requires(min <= max);
                return System.Math.Clamp(value, min, max);
            }

            public void Rename(string name, int retries = 3)
            {
                Pledge.Requires(!string.IsNullOrEmpty(name) && name.Length <= 10);
                Pledge.Requires(retries is >= 0 and < 5);
            }

            public void Describe(string? note)
            {
                Pledge.Requires(note is not null || Balance > 0);
                Pledge.Requires(note == null || note != "");
            }
        }

        """;

    [Theory]
    [InlineData("new Account().Withdraw(-5m);", "amount: -5", "amount > 0", "Account.Withdraw", "-5m")]
    [InlineData("new Account().Withdraw(0);", "amount: 0", "amount > 0", "Account.Withdraw", "0")]
    [InlineData("Account.Clamp(1, 10, 0);", "min: 10, max: 0", "min <= max", "Account.Clamp", "10")]
    [InlineData("Account.Clamp(1, max: 0, min: 10);", "min: 10, max: 0", "min <= max", "Account.Clamp", "10")]
    [InlineData("new Account().Rename(\"\");", "name: \"\"", "!string.IsNullOrEmpty(name) && name.Length <= 10", "Account.Rename", "\"\"")]
    [InlineData("new Account().Rename(\"a very long name\");", "name: \"a very long name\"", "!string.IsNullOrEmpty(name) && name.Length <= 10", "Account.Rename", "\"a very long name\"")]
    [InlineData("new Account().Rename(\"ok\", 7);", "retries: 7", "retries is >= 0 and < 5", "Account.Rename", "7")]
    [InlineData("new Account().Rename(\"ok\", -1);", "retries: -1", "retries is >= 0 and < 5", "Account.Rename", "-1")]
    [InlineData("new Account().Describe(\"\");", "note: \"\"", "note == null || note != \"\"", "Account.Describe", "\"\"")]
    public async Task A_constant_that_violates_a_precondition_is_PLEDGE008(string call, string expectedArguments, string expectedContract, string expectedMember, string expectedSpan)
    {
        var diagnostic = await AnalyzerHarness.SingleAsync(Account + $$"""
            public static class Scenario
            {
                public static void Run() => {{call}}
            }
            """, "PLEDGE008");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal($"Argument {expectedArguments} violates the precondition '{expectedContract}' of '{expectedMember}'", diagnostic.Message());
        Assert.Equal(expectedSpan, diagnostic.SourceText());
        Assert.Single(diagnostic.AdditionalLocations);
    }

    [Theory]
    [InlineData("new Account().Withdraw(5m);")]
    [InlineData("new Account().Withdraw(500m);")]
    [InlineData("new Account().Withdraw(Amount());")]
    [InlineData("Account.Clamp(1, 0, 10);")]
    [InlineData("Account.Clamp(1, 0, Limit);")]
    [InlineData("new Account().Rename(\"ok\");")]
    [InlineData("new Account().Rename(\"ok\", 4);")]
    [InlineData("new Account().Describe(null);")]
    [InlineData("new Account().Describe(\"x\");")]
    public async Task Satisfied_or_undecidable_preconditions_are_silent(string call)
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(Account + $$"""
            public static class Scenario
            {
                public static int Limit;
                public static decimal Amount() => 1m;
                public static void Run() => {{call}}
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_precondition_with_no_parameters_is_never_blamed_on_arguments()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync("""
            using PledgeContracts;

            public sealed class Holder
            {
                public bool Open;

                public void M(int x)
                {
                    Pledge.Requires(Open);
                    Pledge.Requires(false);
                }

                public void Call() => M(1);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Preconditions_are_read_across_files()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync("""
            public static class Scenario
            {
                public static void Run() => new Account().Withdraw(-1m);
            }
            """, extraSources: Account);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PLEDGE008", diagnostic.Id);
        Assert.Equal("/project/Source0.cs", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal("/project/Source1.cs", diagnostic.AdditionalLocations[0].GetLineSpan().Path);
    }
}
