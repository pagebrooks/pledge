namespace PledgeContracts;

// The member returned, or threw, without delivering what it promised. Blame: callee.
public sealed class PostconditionViolationException(PledgeViolation violation)
    : BrokenPledgeException(violation, PledgeKind.Postcondition)
{
}
