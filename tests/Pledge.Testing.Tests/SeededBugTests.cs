using PledgeContracts.Testing.Tests.Domain;

namespace PledgeContracts.Testing.Tests;

// MVP exit criterion 4: the property-test bridge finds a seeded bug the unit tests miss. RoundingAccount rounds
// withdrawals to cents. These unit tests are the ones a developer would plausibly write, and they all pass.
// The property tests run 500 inputs rather than the default 100: the preconditions reject about nine random
// decimal pairs in ten, and 500 makes a run with nothing accepted vanishingly unlikely.
public sealed class SeededBugTests
{
    [Theory]
    [InlineData(100, 30, 70)]
    [InlineData(100, 100, 0)]
    [InlineData(50.25, 12.50, 37.75)]
    [InlineData(0.10, 0.01, 0.09)]
    public void Unit_tests_with_whole_cents_miss_the_bug(decimal balance, decimal amount, decimal expected)
    {
        var account = new RoundingAccount(balance);

        Assert.Equal(expected, account.Withdraw(amount));
        Assert.Equal(expected, account.Balance);
    }

    [Fact]
    public void Unit_tests_of_the_preconditions_pass_too()
    {
        var account = new RoundingAccount(10m);

        Assert.Throws<PreconditionViolationException>(() => account.Withdraw(20m));
    }

    [Fact]
    public async Task The_property_test_finds_it_and_names_the_broken_postcondition()
    {
        var exception = await Assert.ThrowsAsync<PledgePropertyException>(() => PledgeProperties.CheckAsync<RoundingAccount>(new PledgePropertyOptions { Iterations = 500 }));

        var counterexample = Assert.IsType<PledgeCounterexample>(exception.Counterexample);
        Assert.Equal("RoundingAccount.Withdraw(decimal)", counterexample.Member.ToString());
        Assert.Equal(PledgeOutcomeKind.Broken, counterexample.Outcome.Kind);
        Assert.Equal("Balance == Old(Balance) - amount", counterexample.Outcome.Violation!.Expression);
        Assert.StartsWith("new RoundingAccount(", counterexample.Receiver, StringComparison.Ordinal);
        var amount = Assert.Single(counterexample.Arguments);
        Assert.Equal("amount", amount.Key);

        // The shrunk amount really does have digits past the cents, which is the bug.
        var value = decimal.Parse(amount.Value.TrimEnd('m'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotEqual(decimal.Round(value, 2), value);

        Assert.Contains("RoundingAccount.Withdraw(decimal) broke its pledge. Postcondition of RoundingAccount.Withdraw violated: Balance == Old(Balance) - amount", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"amount = {amount.Value}", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"Replay this input with Seed = \"{counterexample.Seed}\" and Iterations = 1.", exception.Message, StringComparison.Ordinal);
    }

    // CsCheck starts a seeded run with the seed's own sample, so one iteration replays exactly the counterexample;
    // with more, it goes on looking for a smaller one.
    [Fact]
    public async Task The_printed_seed_replays_the_same_counterexample()
    {
        var first = await Assert.ThrowsAsync<PledgePropertyException>(() => PledgeProperties.CheckAsync<RoundingAccount>(new PledgePropertyOptions { Iterations = 500 }));
        var seed = Assert.IsType<string>(first.Counterexample!.Seed);

        var replay = await Assert.ThrowsAsync<PledgePropertyException>(() => PledgeProperties.CheckAsync<RoundingAccount>(new PledgePropertyOptions { Seed = seed, Iterations = 1 }));

        Assert.Equal(first.Counterexample.Receiver, replay.Counterexample!.Receiver);
        Assert.Equal(first.Counterexample.Arguments, replay.Counterexample.Arguments);
    }
}
