using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace PledgeContracts.Testing.Xunit;

// Supplies one theory row per pledged member of Type, so each member is its own test in the runner:
//
//     [Theory, PledgedMembers(typeof(Account))]
//     public Task Pledges_hold(PledgeCase pledge) => pledge.CheckAsync();
//
// Members the generator could not probe are listed as skipped, with the reason.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PledgedMembersAttribute(Type type) : DataAttribute
{
    public Type Type { get; } = type ?? throw new ArgumentNullException(nameof(type));

    public int Iterations { get; set; } = 100;

    public string? Seed { get; set; }

    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        IReadOnlyCollection<ITheoryDataRow> rows =
        [
            .. PledgeProperties.Members(Type).Select(member =>
            {
                var row = new TheoryDataRow<PledgeCase>(new PledgeCase(member, Iterations, Seed));
                return member.Probe is null ? row.WithSkip(member.UnsupportedReason!) : row;
            }),
        ];

        return new(rows);
    }

    public override bool SupportsDiscoveryEnumeration() => true;
}
