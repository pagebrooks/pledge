using System.Reflection;

namespace PledgeContracts.Roslyn.Tests;

public sealed class AsyncGenerationTests
{
    private const string Account = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using PledgeContracts;

        public sealed class Account
        {
            public Account(decimal balance) => Balance = balance;

            public decimal Balance { get; private set; }
            public bool Leaky { get; set; }

            public async Task<decimal> WithdrawAsync(decimal amount)
            {
                Pledge.Requires(amount > 0);
                Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
                Pledge.Ensures(Pledge.Result<decimal>() >= 0);
                await Task.Yield();
                Balance -= Leaky ? amount * 2 : amount;
                return Balance;
            }

            public async Task CloseAsync(CancellationToken token)
            {
                Pledge.Ensures(Balance == 0);
                Pledge.EnsuresOnThrow<Exception>(Balance == Pledge.Old(Balance));
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                Balance = 0;
            }

            public async ValueTask<int> CountAsync()
            {
                Pledge.Ensures(Pledge.Result<int>() > 0);
                await Task.Yield();
                return 1;
            }

            public Task<decimal> PeekAsync()
            {
                Pledge.Requires(Balance >= 0);
                return Task.FromResult(Balance);
            }
        }

        public static class Scenario
        {
            public static object Fine() => new Account(10m).WithdrawAsync(4m).GetAwaiter().GetResult();
            public static object Leaky() => new Account(10m) { Leaky = true }.WithdrawAsync(4m).GetAwaiter().GetResult();
            public static object Negative() => new Account(10m).WithdrawAsync(-1m);
            public static object Cancelled()
            {
                var account = new Account(10m);
                try
                {
                    account.CloseAsync(new CancellationToken(canceled: true)).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    return account.Balance;
                }

                return -1m;
            }
            public static object Closed() { var a = new Account(10m); a.CloseAsync(default).GetAwaiter().GetResult(); return a.Balance; }
            public static object Count() => new Account(0m).CountAsync().AsTask().GetAwaiter().GetResult();
            public static object Peek() => new Account(5m).PeekAsync().GetAwaiter().GetResult();
        }
        """;

    [Fact]
    public void Async_wrappers_check_preconditions_synchronously_and_postconditions_after_the_await()
    {
        var run = GeneratorHarness.Run(Account);
        run.AssertCompiles();
        Assert.Empty(run.GeneratorDiagnostics);

        var withdraw = run.Wrappers.Single(pair => pair.Key.Contains("WithdrawAsync", StringComparison.Ordinal)).Value;
        Assert.Contains("return __Awaited(@this, amount, __old0, __callerFilePath, __callerLineNumber);", withdraw, StringComparison.Ordinal);
        Assert.Contains("private static async global::System.Threading.Tasks.Task<decimal> __Awaited(", withdraw, StringComparison.Ordinal);
        Assert.Contains("__result = await @this.WithdrawAsync(amount);", withdraw, StringComparison.Ordinal);

        var peek = run.Wrappers.Single(pair => pair.Key.Contains("PeekAsync", StringComparison.Ordinal)).Value;
        Assert.DoesNotContain("__Awaited", peek, StringComparison.Ordinal);
        Assert.Contains("return @this.PeekAsync();", peek, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_and_violations_flow_through_the_awaited_task()
    {
        var run = GeneratorHarness.Run(Account);

        Assert.Equal(6m, run.Invoke("Scenario", "Fine"));
        var exception = Assert.Throws<PostconditionViolationException>(() => run.Invoke("Scenario", "Leaky"));
        Assert.Equal("Balance == Old(Balance) - amount", exception.Violation.Expression);
        Assert.Equal(5m, run.Invoke("Scenario", "Peek"));
        Assert.Equal(1, run.Invoke("Scenario", "Count"));
    }

    [Fact]
    public void A_broken_precondition_throws_before_a_task_is_returned()
    {
        var run = GeneratorHarness.Run(Account);

        // The scenario returns the Task without awaiting: a faulted task would come back as a value.
        Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Negative"));
    }

    [Fact]
    public void Cancellation_skips_the_postconditions_and_propagates()
    {
        var run = GeneratorHarness.Run(Account);

        Assert.Equal(10m, run.Invoke("Scenario", "Cancelled"));
        Assert.Equal(0m, run.Invoke("Scenario", "Closed"));
    }

    [Fact]
    public void Non_generic_Task_and_ValueTask_methods_are_awaited_without_a_result()
    {
        var run = GeneratorHarness.Run(Account);

        var close = run.Wrappers.Single(pair => pair.Key.Contains("CloseAsync", StringComparison.Ordinal)).Value;
        Assert.Contains("await @this.CloseAsync(token);", close, StringComparison.Ordinal);
        Assert.DoesNotContain("__result", close, StringComparison.Ordinal);
        Assert.NotNull(run.Load().GetType("Account")!.GetMethod("CloseAsync", BindingFlags.Public | BindingFlags.Instance));
    }
}
