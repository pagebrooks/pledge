using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn.Tests;

public sealed class UncoveredCallAnalyzerTests
{
    private const string Accounts = """
        using System;
        using PledgeContracts;

        public interface IAccount
        {
            decimal Withdraw(decimal amount);
        }

        public class Account : IAccount
        {
            public decimal Balance { get; protected set; } = 100m;

            public virtual decimal Withdraw(decimal amount)
            {
                Pledge.Requires(amount > 0);
                Balance -= amount;
                return Balance;
            }
        }

        public class Base
        {
            public virtual void Close()
            {
            }
        }

        public sealed class Savings : Base
        {
            public bool Closed { get; private set; }

            public override void Close()
            {
                Pledge.Ensures(Closed);
                Closed = true;
            }
        }

        public sealed class Checking : Base
        {
        }

        """;

    [Fact]
    public async Task A_contracted_method_turned_into_a_delegate_is_PLEDGE006()
    {
        var diagnostic = await AnalyzerHarness.SingleAsync(Accounts + """
            public static class Scenario
            {
                public static Func<decimal, decimal> Run(Account account) => account.Withdraw;
            }
            """, "PLEDGE006");

        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal("account.Withdraw", diagnostic.SourceText());
        Assert.Equal(
            "'Account.Withdraw' is turned into a delegate here, and calls through a delegate are not intercepted, so its contracts are not checked on them",
            diagnostic.Message());
    }

    [Fact]
    public async Task A_call_through_an_interface_to_a_contracted_implementation_is_PLEDGE006()
    {
        var diagnostic = await AnalyzerHarness.SingleAsync(Accounts + """
            public static class Scenario
            {
                public static decimal Run(IAccount account) => account.Withdraw(5m);
            }
            """, "PLEDGE006");

        Assert.Equal("Withdraw", diagnostic.SourceText());
        Assert.Equal(
            "This call binds to 'IAccount.Withdraw' and can run 'Account.Withdraw', whose contracts are checked only on calls that bind to 'Account.Withdraw' itself",
            diagnostic.Message());
    }

    [Fact]
    public async Task A_call_through_a_base_method_to_a_contracted_override_is_PLEDGE006()
    {
        var diagnostic = await AnalyzerHarness.SingleAsync(Accounts + """
            public static class Scenario
            {
                public static void Run(Base account) => account.Close();
            }
            """, "PLEDGE006");

        Assert.Contains("binds to 'Base.Close' and can run 'Savings.Close'", diagnostic.Message(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static decimal Run(Account account) => account.Withdraw(5m);")]
    [InlineData("public static void Run(Savings account) => account.Close();")]
    [InlineData("public static void Run(Checking account) => account.Close();")]
    [InlineData("public static string Run() => nameof(Account.Withdraw);")]
    public async Task Calls_that_bind_to_the_contracted_method_or_cannot_reach_it_are_silent(string member)
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(Accounts + $$"""
            public static class Scenario
            {
                {{member}}
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_base_call_from_an_override_is_silent()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(Accounts + """
            public sealed class Audited : Account
            {
                public override decimal Withdraw(decimal amount) => base.Withdraw(amount);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Ignore", null, null, 0)]
    [InlineData("Enforce", "Ignore", "Ignore", 0)]
    [InlineData("Enforce", "Ignore", null, 1)]
    [InlineData("Observe", null, null, 1)]
    public async Task Nothing_is_reported_when_every_kind_is_Ignore(string mode, string? preconditions, string? postconditions, int expected)
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(
            Accounts + """
                public static class Scenario
                {
                    public static Func<decimal, decimal> Run(Account account) => account.Withdraw;
                }
                """,
            new Dictionary<string, string?>
            {
                ["build_property.PledgeMode"] = mode,
                ["build_property.PledgePreconditions"] = preconditions,
                ["build_property.PledgePostconditions"] = postconditions,
            });

        Assert.Equal(expected, diagnostics.Count(diagnostic => diagnostic.Id == "PLEDGE006"));
    }
}
