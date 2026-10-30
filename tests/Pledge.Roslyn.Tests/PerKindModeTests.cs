using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn.Tests;

// PledgePreconditions and PledgePostconditions override PledgeMode per kind; EnsuresOnThrow is a postcondition.
public sealed class PerKindModeTests
{
    private const string Account = PostconditionGenerationTests.Account;

    [Fact]
    public void Ignored_preconditions_leave_the_postconditions_checked()
    {
        var run = GeneratorHarness.Run(Account, preconditions: "Ignore");

        run.AssertCompiles();
        var source = run.SingleWrapper;
        Assert.DoesNotContain("(amount > 0)", source, StringComparison.Ordinal);
        Assert.Contains("__old0", source, StringComparison.Ordinal);
        Assert.Contains("catch (global::System.InvalidOperationException __pledgeThrown)", source, StringComparison.Ordinal);
        Assert.Throws<PostconditionViolationException>(() => run.Invoke("Scenario", "Leaky"));
    }

    [Fact]
    public void Ignored_postconditions_drop_the_snapshots_and_the_try_and_forward_directly()
    {
        var run = GeneratorHarness.Run(Account, postconditions: "Ignore");

        run.AssertCompiles();
        var source = run.SingleWrapper;
        Assert.Contains("(amount > 0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__old", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__result", source, StringComparison.Ordinal);
        Assert.DoesNotContain("try", source.Split("private static decimal __Checked")[1].Split("bool __pledgeOk0")[0], StringComparison.Ordinal);
        Assert.Contains("return @this.Withdraw(amount);", source, StringComparison.Ordinal);

        Assert.Equal(2m, run.Invoke("Scenario", "Leaky"));
        Assert.Equal(-4m, run.Invoke("Scenario", "Overdrawn"));
    }

    [Fact]
    public void Each_kind_reports_through_its_own_mode()
    {
        var run = GeneratorHarness.Run(Account, preconditions: "EnforceFailFast", postconditions: "Observe");

        run.AssertCompiles();
        var source = run.SingleWrapper;
        Assert.Contains("global::PledgeContracts.PledgeRuntime.EnforceFailFast(", source, StringComparison.Ordinal);
        Assert.Contains("global::PledgeContracts.PledgeRuntime.Observe(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("global::PledgeContracts.PledgeRuntime.Enforce(", source, StringComparison.Ordinal);

        // Observed postconditions report and carry on: the leaky withdrawal returns its wrong balance.
        Assert.Equal(2m, run.Invoke("Scenario", "Leaky"));
    }

    [Fact]
    public void A_kind_can_be_switched_on_while_PledgeMode_is_Ignore()
    {
        var run = GeneratorHarness.Run(Account, mode: "Ignore", preconditions: "Enforce");

        run.AssertCompiles();
        var source = run.SingleWrapper;
        Assert.Contains("(amount > 0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__old", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_kind_ignored_generates_nothing_and_reports_nothing()
    {
        var run = GeneratorHarness.Run(Account, mode: "Enforce", preconditions: "Ignore", postconditions: "Ignore");

        run.AssertCompiles();
        Assert.Empty(run.Wrappers);
        Assert.Empty(run.GeneratorDiagnostics);
    }

    [Fact]
    public void A_method_with_only_ignored_kinds_reports_no_PLEDGE006()
    {
        var run = GeneratorHarness.Run("""
            using PledgeContracts;

            public sealed class Holder
            {
                public int M(int x)
                {
                    Pledge.Requires(x > 0);
                    return x;
                }
            }
            """, preconditions: "Ignore");

        run.AssertCompiles();
        Assert.Empty(run.GeneratorDiagnostics);
    }

    [Fact]
    public void Metadata_records_every_contract_whatever_the_modes()
    {
        var run = GeneratorHarness.Run(Account, preconditions: "Ignore", postconditions: "Ignore");

        Assert.Contains("global::PledgeContracts.Requires(typeof(global::Account), \"Withdraw(decimal)\", \"amount > 0\")", run.Metadata, StringComparison.Ordinal);
        Assert.Contains("global::PledgeContracts.Ensures(", run.Metadata, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Enforce")]
    [InlineData("", "Enforce")]
    [InlineData("observe", "Observe")]
    public void Per_kind_values_fall_back_to_PledgeMode_and_ignore_case(string? preconditions, string expected)
    {
        var run = GeneratorHarness.Run(Account, mode: "Enforce", preconditions: preconditions, postconditions: "Ignore");

        Assert.Contains($"global::PledgeContracts.PledgeRuntime.{expected}(", run.SingleWrapper, StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Hidden, run.GeneratorDiagnostics.Select(d => d.Severity).DefaultIfEmpty(DiagnosticSeverity.Hidden).Max());
    }
}
