namespace PledgeContracts;

public enum PledgeOutcomeKind
{
    // The preconditions held, the member ran, and every postcondition held.
    Kept,

    // A precondition did not hold, so the call was never made. For a property test this is a discarded input.
    Rejected,

    // The member ran and a postcondition (or EnsuresOnThrow) did not hold, or a contract expression threw.
    Broken,

    // The member threw. IsDeclared says whether an EnsuresOnThrow clause covered the exception (and held).
    Threw,
}

// What one probed call did. Produced by generated probes (see PledgeRegistry); the testing bridge turns these into
// passes, discards and counterexamples.
public sealed class PledgeOutcome
{
    private PledgeOutcome(PledgeOutcomeKind kind, PledgeViolation? violation, Exception? exception, bool isDeclared, object? result)
    {
        Kind = kind;
        Violation = violation;
        Exception = exception;
        IsDeclared = isDeclared;
        Result = result;
    }

    public PledgeOutcomeKind Kind { get; }

    // Rejected: the precondition that failed. Broken: the contract that failed or threw.
    public PledgeViolation? Violation { get; }

    // Threw: the exception that left the member.
    public Exception? Exception { get; }

    public bool IsDeclared { get; }

    // Kept: the member's return value (the awaited value for Task<T> and ValueTask<T>), or null for void.
    public object? Result { get; }

    public static PledgeOutcome Kept(object? result) => new(PledgeOutcomeKind.Kept, null, null, false, result);

    public static PledgeOutcome Rejected(PledgeViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        return new(PledgeOutcomeKind.Rejected, violation, null, false, null);
    }

    public static PledgeOutcome Broken(PledgeViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        return new(PledgeOutcomeKind.Broken, violation, violation.Thrown, false, null);
    }

    public static PledgeOutcome Threw(Exception exception, bool isDeclared)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(PledgeOutcomeKind.Threw, null, exception, isDeclared, null);
    }

    public override string ToString() => Kind switch
    {
        PledgeOutcomeKind.Kept => "Kept",
        PledgeOutcomeKind.Rejected => $"Rejected: {Violation}",
        PledgeOutcomeKind.Broken => $"Broken: {Violation}",
        _ => $"Threw {(IsDeclared ? "declared" : "undeclared")} {Exception!.GetType().Name}: {Exception.Message}",
    };
}
