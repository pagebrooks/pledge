namespace PledgeContracts.Roslyn.Tests;

// A check skips the try/catch around its expression only when the expression cannot throw; everything else keeps it,
// so an exception from the expression is still reported as the contract's failure with the exception as its Cause.
public sealed class ThrowAnalysisTests
{
    private const string Subject = """
        using System;
        using System.Collections.Generic;
        using PledgeContracts;

        public readonly struct Point
        {
            public Point(int x) => X = x;

            public int X { get; }

            public int Doubled => X * 2;
        }

        public class Subject
        {
            public static int Shared;

            public int Count = 1;

            public int Auto { get; set; } = 1;

            public int Computed => Count + 1;

            public virtual int Virtual { get; set; } = 1;

            public string Name { get; set; } = "a";

            public int Run(int amount, int? maybe, object value, decimal money, string text, Point point, List<int> items)
            {
                CONTRACT;
                Auto += amount;
                return amount;
            }
        }

        public static class Scenario
        {
            public static object Fine() => new Subject().Run(1, 1, 1, 1m, "a", new Point(1), new List<int> { 1 });

            public static object NullReceiver() => ((Subject)null!).Run(1, 1, 1, 1m, "a", new Point(1), new List<int> { 1 });
        }
        """;

    [Theory]
    [InlineData("Pledge.Requires(amount > 0)")]
    [InlineData("Pledge.Requires(amount > 0 && maybe.HasValue)")]
    [InlineData("Pledge.Requires(!(amount < 0) || -amount < 5)")]
    [InlineData("Pledge.Requires(value is not null)")]
    [InlineData("Pledge.Requires(value is int and > 0)")]
    [InlineData("Pledge.Requires(money > 0)")]
    [InlineData("Pledge.Requires(text != null && text == \"a\")")]
    [InlineData("Pledge.Requires(point.X > 0)")]
    [InlineData("Pledge.Requires((long)amount * 2 + 1 > 0)")]
    [InlineData("Pledge.Requires(maybe > 0)")]
    [InlineData("Pledge.Requires(value as string == null)")]
    [InlineData("Pledge.Ensures(Count > 0)")]
    [InlineData("Pledge.Ensures(Auto == Pledge.Old(Auto) + amount)")]
    [InlineData("Pledge.Ensures(Pledge.Result<int>() >= 0)")]
    [InlineData("Pledge.EnsuresOnThrow<InvalidOperationException>(amount > 0)")]
    public void Expressions_that_cannot_throw_are_checked_without_a_try(string contract)
    {
        var source = Wrapper(contract);

        Assert.Contains("bool __pledgeOk0 = (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__pledgeCause", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pledge.Requires(amount / 2 >= 0)")]
    [InlineData("Pledge.Requires(checked(amount + 1) > 0)")]
    [InlineData("Pledge.Requires(maybe.Value > 0)")]
    [InlineData("Pledge.Requires(items.Count > 0)")]
    [InlineData("Pledge.Requires(text.Length > 0)")]
    [InlineData("Pledge.Requires(Count > 0)")]
    [InlineData("Pledge.Requires(Auto > 0)")]
    [InlineData("Pledge.Requires(money + 1 > 0)")]
    [InlineData("Pledge.Requires(point.Doubled > 0)")]
    [InlineData("Pledge.Requires(Shared >= 0)")]
    [InlineData("Pledge.Requires((int)value > 0)")]
    [InlineData("Pledge.Requires(value is string { Length: > 0 } || amount > 0)")]
    [InlineData("Pledge.Requires(items.Contains(1))")]
    [InlineData("Pledge.Ensures(Computed > 0)")]
    [InlineData("Pledge.Ensures(Virtual > 0)")]
    [InlineData("Pledge.Ensures(Name.Length > 0)")]
    [InlineData("Pledge.EnsuresOnThrow<InvalidOperationException>(Count > 0)")]
    public void Expressions_that_can_throw_keep_the_try(string contract)
    {
        var source = Wrapper(contract);

        Assert.Contains("bool __pledgeOk0;", source, StringComparison.Ordinal);
        Assert.Contains("catch (global::System.Exception __pledgeCause)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_check_without_a_try_still_reports_a_broken_contract()
    {
        var run = GeneratorHarness.Run(Subject.Replace("CONTRACT", "Pledge.Requires(amount > 1)", StringComparison.Ordinal));

        run.AssertCompiles();
        var exception = Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Fine"));
        Assert.Null(exception.Violation.Cause);
    }

    // The interceptor is handed the caller's receiver before the call, so a precondition reading this keeps the try
    // and a null receiver is still reported as the contract's failure.
    [Fact]
    public void A_precondition_reading_a_null_receiver_reports_the_exception_as_its_cause()
    {
        var run = GeneratorHarness.Run(Subject.Replace("CONTRACT", "Pledge.Requires(Count > 0)", StringComparison.Ordinal));

        run.AssertCompiles();
        Assert.Equal(1, run.Invoke("Scenario", "Fine"));
        var exception = Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "NullReceiver"));
        Assert.IsType<NullReferenceException>(exception.Violation.Cause);
    }

    private static string Wrapper(string contract)
    {
        var run = GeneratorHarness.Run(Subject.Replace("CONTRACT", contract, StringComparison.Ordinal));

        run.AssertCompiles();
        Assert.Equal(1, run.Invoke("Scenario", "Fine"));
        return run.SingleWrapper;
    }
}
