namespace PledgeContracts;

// A public member left the object in a state its invariant forbids. Blame: callee.
public sealed class InvariantViolationException(PledgeViolation violation)
    : BrokenPledgeException(violation, PledgeKind.Invariant)
{
}
