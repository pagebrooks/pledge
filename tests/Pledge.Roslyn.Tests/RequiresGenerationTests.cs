using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn.Tests;

public sealed class RequiresGenerationTests
{
    internal const string Account = """
        using PledgeContracts;

        namespace Bank
        {
            public sealed class Account
            {
                public Account(decimal balance) => Balance = balance;

                public decimal Balance { get; private set; }

                public decimal Withdraw(decimal amount)
                {
                    Pledge.Requires(amount > 0);
                    Pledge.Requires(amount <= Balance, "insufficient funds");
                    Balance -= amount;
                    return Balance;
                }
            }
        }
        """;

    [Fact]
    public void Generates_an_interceptor_and_a_checked_wrapper_for_each_call_site()
    {
        var run = GeneratorHarness.Run(Account + """

            public static class Scenario
            {
                public static object Run() => new Bank.Account(10m).Withdraw(50m);
            }
            """);

        run.AssertCompiles();
        Assert.Empty(run.GeneratorDiagnostics);
        var source = run.SingleWrapper;
        Assert.Contains("namespace PledgeContracts.Generated", source, StringComparison.Ordinal);
        Assert.Contains("[global::System.Runtime.CompilerServices.InterceptsLocation(1, \"", source, StringComparison.Ordinal);
        Assert.Contains("public static decimal __Intercept0(this global::Bank.Account @this, decimal amount)", source, StringComparison.Ordinal);
        Assert.Contains("global::PledgeContracts.PledgeRuntime.Enforce(", source, StringComparison.Ordinal);
        Assert.Contains("__pledgeOk1 = (amount <= @this.Balance);", source, StringComparison.Ordinal);
        Assert.Contains("Expression = \"amount <= Balance\",", source, StringComparison.Ordinal);
        Assert.Contains("Message = \"insufficient funds\",", source, StringComparison.Ordinal);
        Assert.Contains("return @this.Withdraw(amount);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Violations_throw_with_the_contract_text_and_the_call_site()
    {
        var scenario = """

            public static class Scenario
            {
                public static object Run() => new Bank.Account(10m).Withdraw(50m);
            }
            """;
        var run = GeneratorHarness.Run(Account + scenario);

        var exception = Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Run"));

        Assert.Equal("amount <= Balance", exception.Violation.Expression);
        Assert.Equal("insufficient funds", exception.Violation.Message);
        Assert.Equal("Account.Withdraw", exception.Violation.Member);
        Assert.Equal(GeneratorHarness.SourcePath, exception.Violation.CallerFilePath);
        Assert.Equal(LineOf(Account + scenario, "Withdraw(50m)"), exception.Violation.CallerLineNumber);
        Assert.Equal(PledgeBlame.Caller, exception.Blame);
    }

    [Fact]
    public void Preconditions_are_checked_in_source_order_and_satisfied_calls_pass_through()
    {
        var run = GeneratorHarness.Run(Account + """

            public static class Scenario
            {
                public static object Negative() => new Bank.Account(10m).Withdraw(-1m);
                public static object Fine() => new Bank.Account(10m).Withdraw(4m);
            }
            """);

        var exception = Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Negative"));
        Assert.Equal("amount > 0", exception.Violation.Expression);
        Assert.Equal(6m, run.Invoke("Scenario", "Fine"));
    }

    [Fact]
    public void Static_methods_are_called_through_their_type()
    {
        var run = GeneratorHarness.Run("""
            using PledgeContracts;

            namespace Util
            {
                public static class Numbers
                {
                    public static int Clamp(int value, int min, int max)
                    {
                        Pledge.Requires(min <= max);
                        return System.Math.Clamp(value, min, max);
                    }
                }
            }

            public static class Scenario
            {
                public static object Fine() => Util.Numbers.Clamp(15, 0, 10);
                public static object Broken() => Util.Numbers.Clamp(15, 10, 0);
            }
            """);

        Assert.Contains("return global::Util.Numbers.Clamp(value, min, max);", run.SingleWrapper, StringComparison.Ordinal);
        Assert.Equal(10, run.Invoke("Scenario", "Fine"));
        Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Broken"));
    }

    [Fact]
    public void Void_methods_and_ref_parameters_are_forwarded()
    {
        var run = GeneratorHarness.Run("""
            using PledgeContracts;

            public sealed class Counter
            {
                public int Value;

                public void Add(int amount, ref int log, out bool changed)
                {
                    Pledge.Requires(amount >= 0);
                    Value += amount;
                    log += 1;
                    changed = amount > 0;
                }
            }

            public static class Scenario
            {
                public static object Run()
                {
                    var counter = new Counter();
                    var log = 0;
                    counter.Add(3, ref log, out var changed);
                    return (counter.Value, log, changed);
                }
            }
            """);

        Assert.Equal((3, 1, true), run.Invoke("Scenario", "Run"));
    }

    [Fact]
    public void Message_expressions_are_lifted_with_the_condition()
    {
        var run = GeneratorHarness.Run("""
            using PledgeContracts;

            public sealed class Account
            {
                public void Deposit(decimal amount)
                {
                    Pledge.Requires(amount > 0, $"deposits must be positive, got {amount}");
                }
            }

            public static class Scenario
            {
                public static void Run() => new Account().Deposit(-2m);
            }
            """);

        var exception = Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Run"));

        Assert.Equal("deposits must be positive, got -2", exception.Violation.Message);
    }

    [Fact]
    public void Usings_and_enclosing_namespaces_of_the_declaring_file_are_reimported()
    {
        var run = GeneratorHarness.Run("""
            using System.Collections.Generic;
            using System.Linq;
            using PledgeContracts;

            namespace Shop.Orders
            {
                public enum Status { Open, Closed }

                public static class Rules
                {
                    public static bool IsOpen(Status status) => status == Status.Open;
                }

                public sealed class Order
                {
                    public Status Status { get; set; }

                    public int Total(List<int> lines)
                    {
                        Pledge.Requires(lines.Any());
                        Pledge.Requires(Rules.IsOpen(Status));
                        Pledge.Requires(Status == Status.Open);
                        return lines.Sum();
                    }
                }
            }

            public static class Scenario
            {
                public static object Run() => new Shop.Orders.Order().Total(new System.Collections.Generic.List<int> { 1, 2 });
            }
            """);

        var source = run.SingleWrapper;
        Assert.Contains("using global::System.Linq;", source, StringComparison.Ordinal);
        Assert.Contains("using global::Shop.Orders;", source, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Orders.Rules.IsOpen(@this.Status)", source, StringComparison.Ordinal);
        Assert.Contains("@this.Status == global::Shop.Orders.Status.Open", source, StringComparison.Ordinal);
        Assert.Equal(3, run.Invoke("Scenario", "Run"));
    }

    [Fact]
    public void A_contract_expression_that_throws_is_reported_with_its_cause()
    {
        var run = GeneratorHarness.Run("""
            using System.Collections.Generic;
            using System.Linq;
            using PledgeContracts;

            public sealed class Bag
            {
                public int Take(List<int> items)
                {
                    Pledge.Requires(items.First() > 0);
                    return items[0];
                }
            }

            public static class Scenario
            {
                public static object Run() => new Bag().Take(new List<int>());
            }
            """);

        var exception = Assert.Throws<PreconditionViolationException>(() => run.Invoke("Scenario", "Run"));

        Assert.IsType<InvalidOperationException>(exception.Violation.Cause);
        Assert.Same(exception.Violation.Cause, exception.InnerException);
    }

    [Fact]
    public void EnforceFailFast_mode_calls_the_fail_fast_path()
    {
        var run = GeneratorHarness.Run(Account + """

            public static class Scenario
            {
                public static object Run() => new Bank.Account(10m).Withdraw(50m);
            }
            """, mode: "enforcefailfast");

        run.AssertCompiles();
        Assert.Contains("global::PledgeContracts.PledgeRuntime.EnforceFailFast(", run.SingleWrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignore_mode_generates_nothing()
    {
        var run = GeneratorHarness.Run(Account + """

            public static class Scenario
            {
                public static object Run() => new Bank.Account(10m).Withdraw(50m);
            }
            """, mode: "Ignore");

        Assert.Empty(run.Wrappers);
        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(-40m, run.Invoke("Scenario", "Run"));
    }

    [Fact]
    public void Without_the_interceptors_feature_PLEDGE005_is_reported_and_nothing_is_generated()
    {
        var run = GeneratorHarness.Run(Account + """

            public static class Scenario
            {
                public static object Run() => new Bank.Account(10m).Withdraw(50m);
            }
            """, interceptorsEnabled: false);

        Assert.Empty(run.Wrappers);
        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("PLEDGE005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("PledgeContracts.Generated", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Methods_without_call_sites_get_no_wrapper()
    {
        var run = GeneratorHarness.Run(Account);

        run.AssertCompiles();
        Assert.Empty(run.Wrappers);
        Assert.Empty(run.GeneratorDiagnostics);
    }

    [Theory]
    [InlineData("private int _limit = 5; public int M(int x) { Pledge.Requires(x < _limit); return x; }", "'_limit' is private")]
    [InlineData("public int M<T>(T x) { Pledge.Requires(x != null); return 1; }", "generic methods")]
    [InlineData("internal int M(int x) { Pledge.Requires(x > 0); return x; } private void Unused() { }", null)]
    [InlineData("private int M(int x) { Pledge.Requires(x > 0); return x; }", "'M' is not visible")]
    [InlineData("public int M(int x) { Pledge.Requires(Check(x)); return x; bool Check(int v) => v > 0; }", "local function 'Check'")]
    public void Unsupported_targets_report_PLEDGE009_instead_of_generating(string member, string? expectedProblem)
    {
        var run = GeneratorHarness.Run($$"""
            using PledgeContracts;

            public sealed class Holder
            {
                {{member}}

                public int Call() => M(1);
            }
            """);

        run.AssertCompiles();
        if (expectedProblem is null)
        {
            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Single(run.Wrappers);
            return;
        }

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("PLEDGE009", diagnostic.Id);
        Assert.Contains(expectedProblem, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(run.Wrappers);
    }

    [Fact]
    public void Method_models_are_cached_when_an_unrelated_file_changes()
    {
        var first = GeneratorHarness.Run(Account + """

            public static class Scenario
            {
                public static object Run() => new Bank.Account(10m).Withdraw(50m);
            }
            """);

        var second = first.Rerun("public static class Unrelated { public static int One() => 1; }");

        var steps = second.Result.Results.Single().TrackedSteps[PledgeGenerator.TrackingNames.Methods];
        var reasons = steps.SelectMany(step => step.Outputs).Select(output => output.Reason).ToList();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, reason.ToString()));
        second.AssertCompiles();
    }

    private static int LineOf(string source, string needle)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        throw new InvalidOperationException($"'{needle}' not found.");
    }
}
