// These tests break contracts on purpose; PLEDGE008 would otherwise flag every constant that trips a precondition.
#pragma warning disable PLEDGE008

using System.Reflection;

namespace PledgeContracts.Integration.Tests;

public sealed class PostconditionTests
{
    [Fact]
    public void Satisfied_postconditions_let_the_result_through()
    {
        var account = new Account(100m);

        Assert.Equal(70m, account.Withdraw(30m));
        account.Deposit(5m);
        Assert.Equal(75m, account.Balance);
    }

    [Fact]
    public void A_broken_Ensures_blames_the_callee()
    {
        var account = new Account(100m) { Leaky = true };

        var exception = Assert.Throws<PostconditionViolationException>(() => account.Withdraw(30m));

        Assert.Equal(PledgeBlame.Callee, exception.Blame);
        Assert.Equal("Balance == Old(Balance) - amount", exception.Violation.Expression);
        Assert.EndsWith("PostconditionTests.cs", exception.Violation.CallerFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_from_the_body_propagates_when_the_on_throw_contract_holds()
    {
        var account = new Account(100m) { Frozen = true };

        Assert.Throws<InvalidOperationException>(() => account.Withdraw(30m));
        Assert.Equal(100m, account.Balance);
    }

    [Fact]
    public void A_broken_EnsuresOnThrow_wraps_the_original_exception()
    {
        var account = new Account(100m) { Frozen = true, Corrupting = true };

        var exception = Assert.Throws<PostconditionViolationException>(() => account.Withdraw(30m));

        Assert.Equal("Balance == Old(Balance)", exception.Violation.Expression);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Same(exception.InnerException, exception.Violation.Thrown);
    }

    [Fact]
    public async Task Async_postconditions_are_checked_after_the_await()
    {
        var fine = new Account(100m);
        Assert.Equal(70m, await fine.WithdrawAsync(30m, TestContext.Current.CancellationToken));

        var leaky = new Account(100m) { Leaky = true };
        var exception = await Assert.ThrowsAsync<PostconditionViolationException>(() => leaky.WithdrawAsync(30m, TestContext.Current.CancellationToken));
        Assert.Equal("Balance == Old(Balance) - amount", exception.Violation.Expression);
    }

    [Fact]
    public void Async_preconditions_throw_before_a_task_exists()
    {
        var account = new Account(100m);

        // The call must throw before any Task exists, so the lambda discards the Task on purpose.
        Assert.Throws<PreconditionViolationException>(() => { _ = account.WithdrawAsync(-1m, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public async Task Cancellation_bypasses_postconditions()
    {
        var account = new Account(100m);

#pragma warning disable xUnit1051 // A pre-cancelled token is the point of this test.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => account.WithdrawAsync(30m, new CancellationToken(canceled: true)));
#pragma warning restore xUnit1051
        Assert.Equal(100m, account.Balance);
    }

    [Fact]
    public void Out_parameters_in_postconditions_see_their_final_value()
    {
        var account = new Account(10m);

        Assert.True(account.TryWithdraw(4m, out var remaining));
        Assert.Equal(6m, remaining);
    }

    [Fact]
    public void Result_can_appear_more_than_once() =>
        Assert.Equal(10, Account.Clamp(15, 0, 10));

    [Fact]
    public void The_assembly_carries_metadata_for_every_contract()
    {
        var assembly = typeof(Account).Assembly;

        var requires = assembly.GetCustomAttributes<RequiresAttribute>().Where(attribute => attribute.DeclaringType == typeof(Account)).ToList();
        Assert.Contains(requires, attribute => attribute.Member == "Withdraw(decimal)" && attribute.Expression == "amount <= Balance");
        Assert.Contains(assembly.GetCustomAttributes<EnsuresAttribute>(), attribute => attribute.Member == "Clamp(int, int, int)" && attribute.Expression == "Result >= min && Result <= max");
        Assert.Contains(assembly.GetCustomAttributes<EnsuresOnThrowAttribute>(), attribute => attribute.Member == "Withdraw(decimal)" && attribute.ExceptionType == typeof(InvalidOperationException));
    }
}
