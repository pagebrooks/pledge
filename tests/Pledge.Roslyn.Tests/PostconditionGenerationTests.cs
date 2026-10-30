namespace PledgeContracts.Roslyn.Tests;

public sealed class PostconditionGenerationTests
{
    // Leaky double-subtracts (breaks Ensures); Corrupting mutates before throwing (breaks EnsuresOnThrow).
    internal const string Account = """
        using System;
        using PledgeContracts;

        public sealed class Account
        {
            public Account(decimal balance) => Balance = balance;

            public decimal Balance { get; private set; }
            public bool Frozen { get; set; }
            public bool Leaky { get; set; }
            public bool Corrupting { get; set; }
            public bool Cancelling { get; set; }

            public decimal Withdraw(decimal amount)
            {
                Pledge.Requires(amount > 0);
                Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
                Pledge.Ensures(Pledge.Result<decimal>() >= 0, "never overdrawn");
                Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));
                if (Cancelling)
                {
                    Balance = -1m;
                    throw new OperationCanceledException();
                }

                if (Frozen)
                {
                    if (Corrupting)
                    {
                        Balance = -1m;
                    }

                    throw new InvalidOperationException("account is frozen");
                }

                Balance -= Leaky ? amount * 2 : amount;
                return Balance;
            }
        }

        public static class Scenario
        {
            public static object Fine() => new Account(10m).Withdraw(4m);
            public static object Leaky() => new Account(10m) { Leaky = true }.Withdraw(4m);
            public static object Overdrawn() => new Account(10m).Withdraw(14m);
            public static object Frozen() => new Account(10m) { Frozen = true }.Withdraw(4m);
            public static object Corrupting() => new Account(10m) { Frozen = true, Corrupting = true }.Withdraw(4m);
            public static object Cancelling() => new Account(10m) { Cancelling = true }.Withdraw(4m);
        }
        """;

    [Fact]
    public void Generated_wrapper_snapshots_once_and_checks_after_the_body()
    {
        var run = GeneratorHarness.Run(Account);
        run.AssertCompiles();
        Assert.Empty(run.GeneratorDiagnostics);

        var source = run.SingleWrapper;
        Assert.Equal(1, CountOf(source, "decimal __old0 = (@this.Balance);"));
        Assert.Contains("__result = @this.Withdraw(amount);", source, StringComparison.Ordinal);
        Assert.Contains("__pledgeOk1 = (@this.Balance == __old0 - amount);", source, StringComparison.Ordinal);
        Assert.Contains("__pledgeOk2 = (__result >= 0);", source, StringComparison.Ordinal);
        Assert.Contains("Expression = \"Balance == Old(Balance) - amount\",", source, StringComparison.Ordinal);
        Assert.Contains("Expression = \"Result >= 0\",", source, StringComparison.Ordinal);
        Assert.Contains("catch (global::System.OperationCanceledException)", source, StringComparison.Ordinal);
        Assert.Contains("catch (global::System.InvalidOperationException __pledgeThrown)", source, StringComparison.Ordinal);
        Assert.Contains("Thrown = __pledgeThrown,", source, StringComparison.Ordinal);
        Assert.Contains("return __result;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Satisfied_postconditions_let_the_result_through() =>
        Assert.Equal(6m, GeneratorHarness.Run(Account).Invoke("Scenario", "Fine"));

    [Fact]
    public void A_broken_Ensures_blames_the_callee()
    {
        var exception = Assert.Throws<PostconditionViolationException>(() => GeneratorHarness.Run(Account).Invoke("Scenario", "Leaky"));

        Assert.Equal("Balance == Old(Balance) - amount", exception.Violation.Expression);
        Assert.Equal(PledgeBlame.Callee, exception.Blame);
        Assert.Equal("Account.Withdraw", exception.Violation.Member);
        Assert.Equal(GeneratorHarness.SourcePath, exception.Violation.CallerFilePath);
    }

    [Fact]
    public void Result_stands_for_the_return_value()
    {
        var exception = Assert.Throws<PostconditionViolationException>(() => GeneratorHarness.Run(Account).Invoke("Scenario", "Overdrawn"));

        Assert.Equal("Result >= 0", exception.Violation.Expression);
        Assert.Equal("never overdrawn", exception.Violation.Message);
    }

    [Fact]
    public void An_exception_from_the_body_propagates_untouched_when_the_on_throw_contract_holds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GeneratorHarness.Run(Account).Invoke("Scenario", "Frozen"));

        Assert.Equal("account is frozen", exception.Message);
    }

    [Fact]
    public void A_broken_EnsuresOnThrow_wraps_the_original_exception()
    {
        var exception = Assert.Throws<PostconditionViolationException>(() => GeneratorHarness.Run(Account).Invoke("Scenario", "Corrupting"));

        Assert.Equal("Balance == Old(Balance)", exception.Violation.Expression);
        var thrown = Assert.IsType<InvalidOperationException>(exception.Violation.Thrown);
        Assert.Same(thrown, exception.InnerException);
        Assert.Contains("The member exited with InvalidOperationException", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_bypasses_the_on_throw_checks() =>
        Assert.Throws<OperationCanceledException>(() => GeneratorHarness.Run(Account).Invoke("Scenario", "Cancelling"));

    [Fact]
    public void A_contract_naming_a_cancellation_exception_is_checked_instead_of_bypassed()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using PledgeContracts;

            public sealed class Job
            {
                public int Progress { get; private set; }

                public void Run()
                {
                    Pledge.EnsuresOnThrow<OperationCanceledException>(Progress == 0);
                    Progress = 50;
                    throw new OperationCanceledException();
                }
            }

            public static class Scenario
            {
                public static void Run() => new Job().Run();
            }
            """);

        Assert.DoesNotContain("catch (global::System.OperationCanceledException)\n", run.SingleWrapper, StringComparison.Ordinal);
        var exception = Assert.Throws<PostconditionViolationException>(() => run.Invoke("Scenario", "Run"));
        Assert.IsType<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public void On_throw_clauses_are_ordered_derived_first_so_all_are_reachable()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using PledgeContracts;

            public sealed class Parser
            {
                public int Position { get; private set; }

                public int Parse(string text)
                {
                    Pledge.EnsuresOnThrow<Exception>(Position == 0, "any failure rewinds");
                    Pledge.EnsuresOnThrow<ArgumentException>(Position == 0, "bad input rewinds");
                    Pledge.EnsuresOnThrow<ArgumentNullException>(text == null);
                    if (text == null) throw new ArgumentNullException(nameof(text));
                    Position = 3;
                    throw new FormatException();
                }
            }

            public static class Scenario
            {
                public static object Run() => new Parser().Parse("x");
                public static object Null() => new Parser().Parse(null!);
            }
            """);

        var source = run.SingleWrapper;
        Assert.True(
            source.IndexOf("catch (global::System.ArgumentNullException", StringComparison.Ordinal) < source.IndexOf("catch (global::System.ArgumentException", StringComparison.Ordinal)
            && source.IndexOf("catch (global::System.ArgumentException", StringComparison.Ordinal) < source.IndexOf("catch (global::System.Exception __pledgeThrown)", StringComparison.Ordinal));

        var violated = Assert.Throws<PostconditionViolationException>(() => run.Invoke("Scenario", "Run"));
        Assert.Equal("any failure rewinds", violated.Violation.Message);
        Assert.IsType<FormatException>(violated.InnerException);
        Assert.Throws<ArgumentNullException>(() => run.Invoke("Scenario", "Null"));
    }

    [Fact]
    public void Snapshots_are_not_taken_when_a_precondition_fails()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using PledgeContracts;

            public sealed class Box
            {
                public int Value { get; private set; }
                public int Explode() => throw new InvalidOperationException("snapshot taken");

                public void Set(int value)
                {
                    Pledge.Requires(value >= 0);
                    Pledge.Ensures(Value == Pledge.Old(Explode()) + value);
                    Value = value;
                }
            }

            public static class Scenario
            {
                public static void Run() => new Box().Set(-1);
            }
            """);

        Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Run"));
    }

    [Fact]
    public void A_postcondition_that_throws_is_reported_with_its_cause()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using PledgeContracts;

            public sealed class Bag
            {
                public List<int> Items { get; } = new();

                public void Clear()
                {
                    Pledge.Ensures(Items.First() == 0);
                    Items.Clear();
                }
            }

            public static class Scenario
            {
                public static void Run() => new Bag().Clear();
            }
            """);

        var exception = Assert.Throws<PostconditionViolationException>(() => run.Invoke("Scenario", "Run"));
        Assert.IsType<InvalidOperationException>(exception.Violation.Cause);
    }

    [Fact]
    public void Old_of_a_value_type_is_a_copy_and_out_parameters_see_their_final_value()
    {
        var run = GeneratorHarness.Run("""
            using PledgeContracts;

            public sealed class Counter
            {
                public int Count { get; private set; }

                public void Bump(int by, out int before)
                {
                    Pledge.Requires(by > 0);
                    Pledge.Ensures(Count == Pledge.Old(Count) + by);
                    Pledge.Ensures(before == Pledge.Old(Count));
                    before = Count;
                    Count += by;
                }
            }

            public static class Scenario
            {
                public static object Run()
                {
                    var counter = new Counter();
                    counter.Bump(2, out var first);
                    counter.Bump(3, out var second);
                    return (first, second, counter.Count);
                }
            }
            """);

        Assert.Equal((0, 2, 5), run.Invoke("Scenario", "Run"));
    }

    [Theory]
    [InlineData("public int M(int x) { Pledge.Ensures(Pledge.Result<string>() != null); return x; }", "must name the method's result type: Result<int>()")]
    [InlineData("public void M(int x) { Pledge.Ensures(Pledge.Result<int>() > 0); }", "returns nothing")]
    [InlineData("public int M(int x) { Pledge.Requires(Pledge.Old(x) > 0); return x; }", "Old(...) is only meaningful in a postcondition")]
    [InlineData("public int M(int x) { Pledge.Requires(Pledge.Result<int>() > 0); return x; }", "only meaningful in Pledge.Ensures")]
    [InlineData("public int M(int x) { Pledge.EnsuresOnThrow<System.Exception>(Pledge.Result<int>() > 0); return x; }", "has no value when the member throws")]
    [InlineData("public int M(int x) { Pledge.Ensures(Pledge.Old(Pledge.Result<int>()) > 0); return x; }", "cannot appear inside Pledge.Old")]
    [InlineData("public async void M(int x) { Pledge.Ensures(x > 0); await System.Threading.Tasks.Task.Yield(); }", "async void methods cannot carry postconditions")]
    public void Misused_Result_and_Old_report_PLEDGE009(string member, string expectedProblem)
    {
        var run = GeneratorHarness.Run($$"""
            using PledgeContracts;

            public sealed class Holder
            {
                {{member}}

                public void Call() => M(1);
            }
            """);

        run.AssertCompiles();
        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("PLEDGE009", diagnostic.Id);
        Assert.Contains(expectedProblem, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(run.Wrappers);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var index = text.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
