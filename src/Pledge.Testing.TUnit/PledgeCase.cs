namespace PledgeContracts.Testing.TUnit;

// One pledged member as a test argument; its display name is the member.
public sealed class PledgeCase
{
    internal PledgeCase(PledgedMember member, int iterations, string? seed)
    {
        Member = member;
        Iterations = iterations;
        Seed = seed;
    }

    public PledgedMember Member { get; }

    public int Iterations { get; }

    public string? Seed { get; }

    // Runs the property test with the attribute's iterations and seed, writing the summary to the test's output.
    public Task CheckAsync() => CheckAsync(new PledgePropertyOptions { Iterations = Iterations, Seed = Seed });

    // Runs it with these options instead; Output defaults to the test's output when it is not set.
    public Task CheckAsync(PledgePropertyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (Member.Probe is null)
        {
            Skip.Test(Member.UnsupportedReason!);
        }

        options.Output ??= line => TestContext.Current?.Output.WriteLine(line);
        return PledgeProperties.CheckAsync(Member, options);
    }

    public override string ToString() => Member.ToString();
}
