using PledgeContracts.Testing.Tests.Domain;
using PledgeContracts.Testing.Xunit;
using Xunit.Sdk;

namespace PledgeContracts.Testing.Tests;

public sealed class XunitAdapterTests
{
    // The adapter as a consumer uses it: one test per member of Account in the runner.
    [Theory]
    [PledgedMembers(typeof(Account), Iterations = 200)]
    public Task Account_pledges_hold(PledgeCase pledge) => pledge.CheckAsync();

    [Fact]
    public async Task Each_member_is_a_row_and_unprobeable_members_are_skipped_with_the_reason()
    {
        var attribute = new PledgedMembersAttribute(typeof(Ledger));

        var rows = await attribute.GetData(typeof(XunitAdapterTests).GetMethod(nameof(Account_pledges_hold))!, new DisposalTracker());

        Assert.Equal(4, rows.Count);
        var skipped = Assert.Single(rows, row => row.Skip is not null);
        Assert.Equal("parameter 'values' is a ReadOnlySpan<int>, which cannot be passed as an object", skipped.Skip);
        Assert.Equal("Ledger.First(System.ReadOnlySpan<int>)", Assert.IsType<PledgeCase>(Assert.Single(skipped.GetData())).ToString());
        Assert.True(attribute.SupportsDiscoveryEnumeration());
    }

    [Fact]
    public async Task A_case_survives_serialization_so_runners_can_list_and_rerun_it()
    {
        var rows = await new PledgedMembersAttribute(typeof(RoundingAccount)) { Iterations = 50, Seed = "abc" }.GetData(typeof(XunitAdapterTests).GetMethod(nameof(Account_pledges_hold))!, new DisposalTracker());
        var original = Assert.IsType<PledgeCase>(Assert.Single(Assert.Single(rows).GetData()));

        var info = new SerializationInfo();
        ((IXunitSerializable)original).Serialize(info);
#pragma warning disable CS0618 // The parameterless constructor is xUnit's, and so is this round trip.
        var copy = new PledgeCase();
#pragma warning restore CS0618
        ((IXunitSerializable)copy).Deserialize(info);

        Assert.Same(original.Member, copy.Member);
        Assert.Equal(50, copy.Iterations);
        Assert.Equal("abc", copy.Seed);
    }

    [Fact]
    public async Task A_failing_case_throws_the_counterexample()
    {
        var rows = await new PledgedMembersAttribute(typeof(RoundingAccount)) { Iterations = 500 }.GetData(typeof(XunitAdapterTests).GetMethod(nameof(Account_pledges_hold))!, new DisposalTracker());
        var pledge = Assert.IsType<PledgeCase>(Assert.Single(Assert.Single(rows).GetData()));

        var exception = await Assert.ThrowsAsync<PledgePropertyException>(pledge.CheckAsync);

        Assert.Equal("Balance == Old(Balance) - amount", exception.Counterexample!.Outcome.Violation!.Expression);
    }

    private sealed class SerializationInfo : IXunitSerializationInfo
    {
        private readonly Dictionary<string, object?> _values = [];

        public void AddValue(string key, object? value, Type? valueType = null) => _values[key] = value;

        public object? GetValue(string key) => _values[key];
    }
}
