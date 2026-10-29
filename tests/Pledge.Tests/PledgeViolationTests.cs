namespace PledgeContracts.Tests;

public sealed class PledgeViolationTests
{
    [Theory]
    [InlineData(PledgeKind.Precondition, PledgeBlame.Caller)]
    [InlineData(PledgeKind.Postcondition, PledgeBlame.Callee)]
    [InlineData(PledgeKind.Invariant, PledgeBlame.Callee)]
    public void Blame_follows_the_kind(PledgeKind kind, PledgeBlame expected)
    {
        var violation = new PledgeViolation { Kind = kind, Expression = "x", Member = "T.M" };

        Assert.Equal(expected, violation.Blame);
    }

    [Fact]
    public void Precondition_text_names_the_caller_and_the_call_site()
    {
        var violation = new PledgeViolation
        {
            Kind = PledgeKind.Precondition,
            Expression = "amount > 0",
            Member = "Account.Withdraw",
            CallerFilePath = "/src/Program.cs",
            CallerLineNumber = 42,
        };

        Assert.Equal("Precondition of Account.Withdraw violated by its caller: amount > 0 (at /src/Program.cs:42)", violation.ToString());
    }

    [Fact]
    public void Message_is_shown_with_the_expression_in_brackets()
    {
        var violation = new PledgeViolation
        {
            Kind = PledgeKind.Postcondition,
            Expression = "Balance == Old(Balance) - amount",
            Member = "Account.Withdraw",
            Message = "balance must drop by the amount",
        };

        Assert.Equal(
            "Postcondition of Account.Withdraw violated: balance must drop by the amount [Balance == Old(Balance) - amount]",
            violation.ToString());
    }

    [Fact]
    public void Message_equal_to_the_expression_is_not_repeated()
    {
        var violation = new PledgeViolation { Kind = PledgeKind.Invariant, Expression = "Balance >= 0", Member = "Account", Message = "Balance >= 0" };

        Assert.Equal("Invariant of Account violated: Balance >= 0", violation.ToString());
    }

    [Fact]
    public void Cause_is_described_when_the_expression_threw()
    {
        var violation = new PledgeViolation
        {
            Kind = PledgeKind.Postcondition,
            Expression = "Items.First() > 0",
            Member = "Bag.Take",
            Cause = new InvalidOperationException("Sequence contains no elements"),
        };

        Assert.Equal(
            "Postcondition of Bag.Take violated: Items.First() > 0. The contract expression threw InvalidOperationException: Sequence contains no elements",
            violation.ToString());
    }
}
