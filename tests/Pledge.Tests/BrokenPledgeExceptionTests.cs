namespace PledgeContracts.Tests;

public sealed class BrokenPledgeExceptionTests
{
    [Theory]
    [InlineData(PledgeKind.Precondition, typeof(PreconditionViolationException))]
    [InlineData(PledgeKind.Postcondition, typeof(PostconditionViolationException))]
    [InlineData(PledgeKind.Invariant, typeof(InvariantViolationException))]
    public void From_picks_the_exception_for_the_kind(PledgeKind kind, Type expected)
    {
        var violation = Violation(kind);

        var exception = BrokenPledgeException.From(violation);

        Assert.IsType(expected, exception);
        Assert.Same(violation, exception.Violation);
        Assert.Equal(kind, exception.Kind);
        Assert.Equal(violation.Blame, exception.Blame);
    }

    [Fact]
    public void Message_is_the_violation_text_and_the_cause_is_the_inner_exception()
    {
        var cause = new InvalidOperationException("boom");
        var violation = Violation(PledgeKind.Postcondition) with { Cause = cause };

        var exception = BrokenPledgeException.From(violation);

        Assert.Equal(violation.ToString(), exception.Message);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public void Kind_specific_exceptions_reject_a_violation_of_another_kind()
    {
        var violation = Violation(PledgeKind.Postcondition);

        var exception = Assert.Throws<ArgumentException>(() => new PreconditionViolationException(violation));

        Assert.Equal("violation", exception.ParamName);
    }

    [Fact]
    public void From_rejects_null() =>
        Assert.Throws<ArgumentNullException>(() => BrokenPledgeException.From(null!));

    private static PledgeViolation Violation(PledgeKind kind) =>
        new() { Kind = kind, Expression = "x > 0", Member = "T.M" };
}
