using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn.Tests;

public sealed class ProbeGenerationTests
{
    private const string Account = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using PledgeContracts;

        namespace Bank
        {
            public sealed class Account
            {
                public Account(decimal balance) => Balance = balance;

                public decimal Balance { get; private set; }

                public bool Frozen { get; set; }

                public decimal Withdraw(decimal amount)
                {
                    Pledge.Requires(amount > 0);
                    Pledge.Requires(amount <= Balance, "insufficient funds");
                    Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
                    Pledge.Ensures(Pledge.Result<decimal>() >= 0);
                    Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));

                    if (Frozen)
                    {
                        throw new InvalidOperationException("frozen");
                    }

                    // The seeded bug: a withdrawal of exactly 13 takes one too many.
                    Balance -= amount == 13m ? amount + 1m : amount;
                    return Balance;
                }

                public void Rename(string name, string? note)
                {
                    Pledge.Requires(name.Length > 0);
                    if (name == "boom")
                    {
                        throw new FormatException(note);
                    }
                }

                public async Task<decimal> DepositAsync(decimal amount, CancellationToken cancellationToken)
                {
                    Pledge.Requires(amount > 0);
                    Pledge.Ensures(Pledge.Result<decimal>() == Pledge.Old(Balance) + amount);
                    await Task.Yield();
                    Balance += amount == 7m ? 0m : amount;
                    return Balance;
                }

                public static int Half(int value)
                {
                    Pledge.Requires(value % 2 == 0);
                    Pledge.Ensures(Pledge.Result<int>() * 2 == value);
                    return value / 2;
                }

                public int Sum(ReadOnlySpan<int> values)
                {
                    Pledge.Requires(values.Length > 0);
                    var total = 0;
                    foreach (var value in values)
                    {
                        total += value;
                    }

                    return total;
                }
            }
        }
        """;

    [Fact]
    public void Every_generable_member_is_registered_with_its_shape_and_contracts()
    {
        var members = Registered(GeneratorHarness.Run(Account));

        Assert.Equal(["DepositAsync(decimal, System.Threading.CancellationToken)", "Half(int)", "Rename(string, string)", "Sum(System.ReadOnlySpan<int>)", "Withdraw(decimal)"], members.Select(member => member.Signature));

        var withdraw = members.Single(member => member.Name == "Withdraw");
        Assert.False(withdraw.IsStatic);
        Assert.Equal(typeof(decimal), withdraw.ResultType);
        var amount = Assert.Single(withdraw.Parameters);
        Assert.Equal(("amount", typeof(decimal), PledgedParameterDirection.Value, false), (amount.Name, amount.Type, amount.Direction, amount.AllowsNull));
        Assert.Equal(
            ["Precondition: amount > 0", "Precondition: amount <= Balance", "Postcondition: Balance == Old(Balance) - amount", "Postcondition: Result >= 0", "Postcondition on InvalidOperationException: Balance == Old(Balance)"],
            withdraw.Contracts.Select(contract => contract.ToString()));

        var rename = members.Single(member => member.Name == "Rename");
        Assert.Null(rename.ResultType);
        Assert.Equal([false, true], rename.Parameters.Select(parameter => parameter.AllowsNull));

        Assert.True(members.Single(member => member.Name == "Half").IsStatic);
        Assert.Equal(typeof(decimal), members.Single(member => member.Name == "DepositAsync").ResultType);
    }

    [Fact]
    public async Task A_call_that_keeps_every_contract_is_Kept_with_its_result()
    {
        var (member, account) = Withdraw(GeneratorHarness.Run(Account), balance: 100m);

        var outcome = await member.Probe!(account, [30m]);

        Assert.Equal(PledgeOutcomeKind.Kept, outcome.Kind);
        Assert.Equal(70m, outcome.Result);
    }

    [Fact]
    public async Task A_failed_precondition_is_Rejected_and_the_member_is_not_called()
    {
        var (member, account) = Withdraw(GeneratorHarness.Run(Account), balance: 10m);

        var outcome = await member.Probe!(account, [20m]);

        Assert.Equal(PledgeOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("amount <= Balance", outcome.Violation!.Expression);
        Assert.Equal("insufficient funds", outcome.Violation.Message);
        Assert.Equal(PledgeKind.Precondition, outcome.Violation.Kind);
        Assert.Equal(10m, Balance(account));
    }

    [Fact]
    public async Task A_failed_postcondition_is_Broken()
    {
        var (member, account) = Withdraw(GeneratorHarness.Run(Account), balance: 100m);

        var outcome = await member.Probe!(account, [13m]);

        Assert.Equal(PledgeOutcomeKind.Broken, outcome.Kind);
        Assert.Equal("Balance == Old(Balance) - amount", outcome.Violation!.Expression);
        Assert.Equal("Account.Withdraw", outcome.Violation.Member);
    }

    [Fact]
    public async Task An_exception_an_EnsuresOnThrow_covers_is_declared_when_the_clause_holds()
    {
        var (member, account) = Withdraw(GeneratorHarness.Run(Account), balance: 100m);
        account.GetType().GetProperty("Frozen")!.SetValue(account, true);

        var outcome = await member.Probe!(account, [5m]);

        Assert.Equal(PledgeOutcomeKind.Threw, outcome.Kind);
        Assert.True(outcome.IsDeclared);
        Assert.IsType<InvalidOperationException>(outcome.Exception);
    }

    [Fact]
    public async Task An_exception_no_contract_covers_is_undeclared()
    {
        var run = GeneratorHarness.Run(Account);
        var type = run.Load().GetType("Bank.Account")!;
        var rename = PledgeRegistry.For(type).Single(member => member.Name == "Rename");

        var outcome = await rename.Probe!(Activator.CreateInstance(type, 0m), ["boom", null]);

        Assert.Equal(PledgeOutcomeKind.Threw, outcome.Kind);
        Assert.False(outcome.IsDeclared);
        Assert.IsType<FormatException>(outcome.Exception);
    }

    [Fact]
    public async Task A_contract_that_throws_is_Broken_with_the_exception_as_its_cause()
    {
        var run = GeneratorHarness.Run(Account);
        var type = run.Load().GetType("Bank.Account")!;
        var rename = PledgeRegistry.For(type).Single(member => member.Name == "Rename");

        var outcome = await rename.Probe!(Activator.CreateInstance(type, 0m), [null, null]);

        Assert.Equal(PledgeOutcomeKind.Broken, outcome.Kind);
        Assert.IsType<NullReferenceException>(outcome.Violation!.Cause);
    }

    [Fact]
    public async Task Async_members_are_awaited_and_their_postconditions_checked()
    {
        var run = GeneratorHarness.Run(Account);
        var type = run.Load().GetType("Bank.Account")!;
        var deposit = PledgeRegistry.For(type).Single(member => member.Name == "DepositAsync");

        var kept = await deposit.Probe!(Activator.CreateInstance(type, 10m), [5m, CancellationToken.None]);
        var broken = await deposit.Probe!(Activator.CreateInstance(type, 10m), [7m, CancellationToken.None]);

        Assert.Equal(PledgeOutcomeKind.Kept, kept.Kind);
        Assert.Equal(15m, kept.Result);
        Assert.Equal(PledgeOutcomeKind.Broken, broken.Kind);
    }

    [Fact]
    public async Task Static_members_take_a_null_receiver()
    {
        var run = GeneratorHarness.Run(Account);
        var half = PledgeRegistry.For(run.Load().GetType("Bank.Account")!).Single(member => member.Name == "Half");

        Assert.Equal(PledgeOutcomeKind.Kept, (await half.Probe!(null, [8])).Kind);
        Assert.Equal(PledgeOutcomeKind.Rejected, (await half.Probe!(null, [7])).Kind);
    }

    [Fact]
    public void A_member_whose_arguments_cannot_be_boxed_is_registered_without_a_probe()
    {
        var sum = Registered(GeneratorHarness.Run(Account)).Single(member => member.Name == "Sum");

        Assert.Null(sum.Probe);
        Assert.Equal("parameter 'values' is a ReadOnlySpan<int>, which cannot be passed as an object", sum.UnsupportedReason);
    }

    [Fact]
    public async Task Probes_check_every_contract_whatever_the_modes()
    {
        var run = GeneratorHarness.Run(Account, mode: "Enforce", postconditions: "Ignore");
        var (member, account) = Withdraw(run, balance: 100m);

        Assert.Equal(PledgeOutcomeKind.Broken, (await member.Probe!(account, [13m])).Kind);
    }

    [Fact]
    public void Members_without_call_sites_still_get_probes()
    {
        var run = GeneratorHarness.Run(Account);

        Assert.Empty(run.Wrappers);
        Assert.Equal(5, run.Probes.Count);
    }

    [Theory]
    [InlineData("Ignore", null, 0)]
    [InlineData("Ignore", "true", 5)]
    [InlineData("Ignore", "on", 5)]
    [InlineData("Observe", null, 5)]
    [InlineData("Enforce", "false", 0)]
    [InlineData("Enforce", "no", 0)]
    public void The_registry_follows_the_modes_unless_PledgeRegistry_says_otherwise(string mode, string? registry, int probes)
    {
        var run = GeneratorHarness.Run(Account, mode: mode, registry: registry);

        Assert.Equal(probes, run.Probes.Count);
        run.AssertCompiles();
    }

    [Fact]
    public void Probes_stay_cached_when_an_unrelated_file_changes()
    {
        var second = GeneratorHarness.Run(Account).Rerun("public static class Unrelated { public static int One() => 1; }");

        var steps = second.Result.Results.Single().TrackedSteps[PledgeGenerator.TrackingNames.Probes];
        var reasons = steps.SelectMany(step => step.Outputs).Select(output => output.Reason).ToList();

        Assert.Equal(5, reasons.Count);
        Assert.All(reasons, reason => Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, reason.ToString()));
    }

    private static IReadOnlyList<PledgedMember> Registered(GeneratorRun run) =>
        PledgeRegistry.For(run.Load().GetType("Bank.Account")!);

    private static (PledgedMember Member, object Account) Withdraw(GeneratorRun run, decimal balance)
    {
        var type = run.Load().GetType("Bank.Account")!;
        var member = PledgeRegistry.For(type).Single(candidate => candidate.Name == "Withdraw");
        return (member, Activator.CreateInstance(type, balance)!);
    }

    private static decimal Balance(object account) => (decimal)account.GetType().GetProperty("Balance")!.GetValue(account)!;
}
