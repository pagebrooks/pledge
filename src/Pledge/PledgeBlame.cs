namespace PledgeContracts;

// A violation is a bug with blame attached: a broken precondition is the caller's, everything else is the callee's.
public enum PledgeBlame
{
    Caller,
    Callee,
}
