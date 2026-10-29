namespace PledgeContracts.Tests;

// These swap the process-wide handler, so they stay in one class (xUnit runs a class's tests in sequence)
// and restore it in a finally. EnforceFailFast is not covered here: it terminates the test host by design.
public sealed class PledgeRuntimeTests
{
    [Fact]
    public void Observe_invokes_the_handler_and_returns()
    {
        var violation = Violation();
        var seen = new List<PledgeViolation>();

        WithHandler(seen.Add, () => PledgeRuntime.Observe(violation));

        Assert.Equal([violation], seen);
    }

    [Fact]
    public void Observe_without_a_handler_is_a_no_op() =>
        WithHandler(null, () => PledgeRuntime.Observe(Violation()));

    [Fact]
    public void Enforce_invokes_the_handler_then_throws_the_kind_specific_exception()
    {
        var violation = Violation();
        var seen = new List<PledgeViolation>();

        WithHandler(seen.Add, () =>
        {
            var exception = Assert.Throws<PreconditionViolationException>(() => PledgeRuntime.Enforce(violation));
            Assert.Same(violation, exception.Violation);
        });

        Assert.Equal([violation], seen);
    }

    [Fact]
    public void Enforce_without_a_handler_still_throws() =>
        WithHandler(null, () => Assert.Throws<PreconditionViolationException>(() => PledgeRuntime.Enforce(Violation())));

    [Fact]
    public void Observe_and_Enforce_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => PledgeRuntime.Observe(null!));
        Assert.Throws<ArgumentNullException>(() => PledgeRuntime.Enforce(null!));
    }

    private static PledgeViolation Violation() =>
        new() { Kind = PledgeKind.Precondition, Expression = "x > 0", Member = "T.M" };

    private static void WithHandler(Action<PledgeViolation>? handler, Action body)
    {
        var previous = PledgeRuntime.ViolationHandler;
        PledgeRuntime.ViolationHandler = handler;
        try
        {
            body();
        }
        finally
        {
            PledgeRuntime.ViolationHandler = previous;
        }
    }
}
