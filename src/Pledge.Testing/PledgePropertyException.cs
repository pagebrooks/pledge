namespace PledgeContracts.Testing;

// A property test found a member breaking its contracts, or could not test it meaningfully. Counterexample is
// the smallest failing input CsCheck shrank to; it is null when the run failed without one, for instance when
// the preconditions rejected every generated input.
public sealed class PledgePropertyException : Exception
{
    public PledgePropertyException(string message)
        : base(message)
    {
    }

    public PledgePropertyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal PledgePropertyException(PledgeCounterexample counterexample)
        : base(counterexample.ToString(), counterexample.Outcome.Violation?.Cause ?? counterexample.Outcome.Exception)
    {
        Counterexample = counterexample;
    }

    public PledgePropertyException()
    {
    }

    public PledgeCounterexample? Counterexample { get; }
}

// One shrunk failing input: the member, what the probe reported, the receiver and arguments as code, and the seed
// that replays the run.
public sealed class PledgeCounterexample
{
    internal PledgeCounterexample(PledgedMember member, PledgeOutcome outcome, string? receiver, IReadOnlyList<KeyValuePair<string, string>> arguments, string? seed, string? summary)
    {
        Member = member;
        Outcome = outcome;
        Receiver = receiver;
        Arguments = arguments;
        Seed = seed;
        Summary = summary;
    }

    public PledgedMember Member { get; }

    public PledgeOutcome Outcome { get; }

    // The receiver as the constructor call that built it, such as "new Account(0.005m)". Null for static members.
    public string? Receiver { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Arguments { get; }

    public string? Seed { get; }

    // CsCheck's account of the run: shrinks, skipped and total samples.
    public string? Summary { get; }

    public override string ToString()
    {
        var lines = new List<string> { $"{Member} broke its pledge. {Describe(Outcome)}", "Counterexample:" };
        if (Receiver is not null)
        {
            lines.Add($"  receiver = {Receiver}");
        }

        lines.AddRange(Arguments.Select(argument => $"  {argument.Key} = {argument.Value}"));
        if (Seed is not null)
        {
            lines.Add($"Replay this input with Seed = \"{Seed}\" and Iterations = 1.{(Summary is null ? string.Empty : $" CsCheck: {Summary}.")}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Describe(PledgeOutcome outcome) => outcome.Kind switch
    {
        PledgeOutcomeKind.Broken => outcome.Violation!.ToString(),
        PledgeOutcomeKind.Threw => $"It threw {outcome.Exception!.GetType().Name}, which no EnsuresOnThrow declares: {outcome.Exception.Message}",
        _ => outcome.ToString(),
    };
}
