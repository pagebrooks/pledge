using System.Runtime.CompilerServices;

namespace PledgeContracts.Integration.Tests;

// These calls are intercepted by the generator at build time, so a passing run proves the package wiring:
// props, targets, PledgeMode, the analyzer reference and the generated code all working together.
public sealed class AccountTests
{
    [Fact]
    public void Satisfied_preconditions_let_the_call_through()
    {
        var account = new Account(100m);

        Assert.Equal(70m, account.Withdraw(30m));
        Assert.Equal(70m, account.Balance);
    }

    [Fact]
    public void A_broken_precondition_blames_the_caller_with_file_and_line()
    {
        var account = new Account(10m);

        var (exception, line) = ThrowsHere(() => account.Withdraw(50m));

        Assert.Equal(PledgeKind.Precondition, exception.Kind);
        Assert.Equal(PledgeBlame.Caller, exception.Blame);
        Assert.Equal("amount <= Balance", exception.Violation.Expression);
        Assert.Equal("insufficient funds", exception.Violation.Message);
        Assert.Equal("Account.Withdraw", exception.Violation.Member);
        Assert.EndsWith("AccountTests.cs", exception.Violation.CallerFilePath, StringComparison.Ordinal);
        Assert.Equal(line, exception.Violation.CallerLineNumber);
        Assert.Equal(10m, account.Balance);
    }

    [Fact]
    public void Preconditions_run_in_source_order()
    {
        var account = new Account(10m);

        var exception = Assert.Throws<PreconditionViolationException>(() => account.Withdraw(-1m));

        Assert.Equal("amount > 0", exception.Violation.Expression);
    }

    [Fact]
    public void Message_expressions_see_the_arguments()
    {
        var account = new Account(0m);

        var exception = Assert.Throws<PreconditionViolationException>(() => account.Deposit(-2m));

        Assert.Equal("deposits must be positive, got -2", exception.Violation.Message);
        Assert.Contains("deposits must be positive, got -2 [amount > 0]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Out_parameters_and_void_results_are_forwarded()
    {
        var account = new Account(10m);

        Assert.True(account.TryWithdraw(4m, out var remaining));
        Assert.Equal(6m, remaining);
        Assert.False(account.TryWithdraw(40m, out remaining));
        Assert.Equal(6m, remaining);
        Assert.Throws<PreconditionViolationException>(() => account.TryWithdraw(0m, out _));
    }

    [Fact]
    public void Static_methods_are_intercepted_too()
    {
        Assert.Equal(10, Account.Clamp(15, 0, 10));
        Assert.Throws<PreconditionViolationException>(() => Account.Clamp(15, 10, 0));
    }

    // The lambda sits on the caller's line, so the intercepted call inside it and the captured line number agree.
    private static (PreconditionViolationException Exception, int Line) ThrowsHere(Func<object> call, [CallerLineNumber] int line = 0) =>
        (Assert.Throws<PreconditionViolationException>(() => call()), line);
}
