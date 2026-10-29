namespace PledgeContracts;

public abstract class BrokenPledgeException : Exception
{
    protected BrokenPledgeException(PledgeViolation violation, PledgeKind expectedKind)
        : base(Describe(violation, expectedKind), violation.Cause)
    {
        Violation = violation;
    }

    public PledgeViolation Violation { get; }

    public PledgeKind Kind => Violation.Kind;

    public PledgeBlame Blame => Violation.Blame;

    public static BrokenPledgeException From(PledgeViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        return violation.Kind switch
        {
            PledgeKind.Precondition => new PreconditionViolationException(violation),
            PledgeKind.Postcondition => new PostconditionViolationException(violation),
            PledgeKind.Invariant => new InvariantViolationException(violation),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation.Kind, "Unknown contract kind."),
        };
    }

    private static string Describe(PledgeViolation violation, PledgeKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(violation);

        if (violation.Kind != expectedKind)
        {
            throw new ArgumentException($"A {expectedKind} exception cannot carry a {violation.Kind} violation.", nameof(violation));
        }

        return violation.ToString();
    }
}
