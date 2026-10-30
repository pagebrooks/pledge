namespace PledgeContracts.Testing.TUnit;

// Supplies one test per pledged member of Type:
//
//     [Test, PledgedMembers(typeof(Account))]
//     public Task Pledges_hold(PledgeCase pledge) => pledge.CheckAsync();
//
// Members the generator could not probe still get a test, which is skipped with the reason when it runs.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PledgedMembersAttribute(Type type) : DataSourceGeneratorAttribute<PledgeCase>
{
    public Type Type { get; } = type ?? throw new ArgumentNullException(nameof(type));

    public int Iterations { get; set; } = 100;

    public string? Seed { get; set; }

    protected override IEnumerable<Func<PledgeCase>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata)
    {
        foreach (var member in PledgeProperties.Members(Type))
        {
            yield return () => new PledgeCase(member, Iterations, Seed);
        }
    }
}
