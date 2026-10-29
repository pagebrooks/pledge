using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace PledgeContracts;

// The three things generated code can do with a violation, one per enforcement mode that runs checks.
// The mode is chosen at build time by the generator, so there is no mode state here; the only runtime
// knob is the handler, which every path invokes first (C++26's handle_contract_violation).
public static class PledgeRuntime
{
    public static Action<PledgeViolation>? ViolationHandler { get; set; }

    [DebuggerStepThrough]
    [StackTraceHidden]
    public static void Observe(PledgeViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        ViolationHandler?.Invoke(violation);
    }

    [DebuggerStepThrough]
    [StackTraceHidden]
    [DoesNotReturn]
    public static void Enforce(PledgeViolation violation)
    {
        Observe(violation);

        throw BrokenPledgeException.From(violation);
    }

    [DebuggerStepThrough]
    [StackTraceHidden]
    [DoesNotReturn]
    public static void EnforceFailFast(PledgeViolation violation)
    {
        Observe(violation);

        var exception = BrokenPledgeException.From(violation);
        Environment.FailFast(exception.Message, exception);
    }
}
