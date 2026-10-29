namespace PledgeContracts;

// The caller passed arguments or state the member does not accept. Blame: caller.
public sealed class PreconditionViolationException(PledgeViolation violation)
    : BrokenPledgeException(violation, PledgeKind.Precondition)
{
}
