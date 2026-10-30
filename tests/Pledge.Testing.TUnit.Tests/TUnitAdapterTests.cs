using PledgeContracts.Testing.Tests.Domain;

namespace PledgeContracts.Testing.TUnit.Tests;

public sealed class TUnitAdapterTests
{
    // The adapter as a consumer uses it: one test per member of Account.
    [Test]
    [PledgedMembers(typeof(Account), Iterations = 200)]
    public Task Account_pledges_hold(PledgeCase pledge) => pledge.CheckAsync();

    // Ledger.First takes a Span, so its test is skipped with the reason rather than failed.
    [Test]
    [PledgedMembers(typeof(Ledger))]
    public Task Ledger_pledges_hold(PledgeCase pledge) => pledge.CheckAsync();

    // The seeded bug, found through TUnit: the case fails with the counterexample.
    [Test]
    [PledgedMembers(typeof(RoundingAccount), Iterations = 500)]
    public async Task The_seeded_rounding_bug_is_found(PledgeCase pledge)
    {
        var exception = await Assert.That(pledge.CheckAsync).Throws<PledgePropertyException>();

        await Assert.That(exception!.Counterexample!.Outcome.Violation!.Expression).IsEqualTo("Balance == Old(Balance) - amount");
    }

    [Test]
    public async Task Cases_are_named_after_their_member()
    {
        var names = PledgeProperties.Members<Account>().Select(member => member.ToString()).ToList();

        await Assert.That(names).IsEquivalentTo(["Account.DepositAsync(decimal, System.Threading.CancellationToken)", "Account.Fee(decimal)", "Account.Withdraw(decimal)"]);
    }
}
