using System.Collections.Concurrent;

namespace PledgeContracts.Roslyn.Tests;

// Enforce calls the process-wide handler before throwing, so tests in other classes running in parallel report
// into it too. This class uses its own type name and filters on it, and swaps the handler back when done.
public sealed class ObserveModeTests
{
    private const string Source = """
        using PledgeContracts;

        public sealed class Observed
        {
            public decimal Balance { get; private set; } = 10m;

            public decimal Withdraw(decimal amount)
            {
                Pledge.Requires(amount <= Balance, "insufficient funds");
                Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
                Balance -= amount * 2;
                return Balance;
            }
        }

        public static class Scenario
        {
            public static object Run() => new Observed().Withdraw(50m);
        }
        """;

    [Fact]
    public void Observe_mode_reports_every_violation_to_the_handler_and_continues()
    {
        var run = GeneratorHarness.Run(Source, mode: "Observe");
        Assert.Contains("global::PledgeContracts.PledgeRuntime.Observe(", run.SingleWrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("PledgeRuntime.Enforce(", run.SingleWrapper, StringComparison.Ordinal);

        var seen = new ConcurrentQueue<PledgeViolation>();
        var previous = PledgeRuntime.ViolationHandler;
        PledgeRuntime.ViolationHandler = seen.Enqueue;
        try
        {
            Assert.Equal(-90m, run.Invoke("Scenario", "Run"));
        }
        finally
        {
            PledgeRuntime.ViolationHandler = previous;
        }

        var violations = seen.Where(violation => violation.Member == "Observed.Withdraw").ToList();
        Assert.Equal(2, violations.Count);
        Assert.Equal(PledgeKind.Precondition, violations[0].Kind);
        Assert.Equal("insufficient funds", violations[0].Message);
        Assert.Equal(PledgeKind.Postcondition, violations[1].Kind);
        Assert.Equal("Balance == Old(Balance) - amount", violations[1].Expression);
    }
}
