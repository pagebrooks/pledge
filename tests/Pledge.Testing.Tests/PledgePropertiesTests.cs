using CsCheck;
using PledgeContracts.Testing.Tests.Domain;

namespace PledgeContracts.Testing.Tests;

public sealed class PledgePropertiesTests
{
    [Fact]
    public async Task A_correct_type_passes_and_reports_what_each_member_saw()
    {
        List<string> output = [];

        await PledgeProperties.CheckAsync<Account>(new PledgePropertyOptions { Iterations = 300, Output = output.Add });

        Assert.Contains(output, line => line.StartsWith("Account.Withdraw(decimal): 300 inputs: ", StringComparison.Ordinal));
        Assert.Contains(output, line => line.StartsWith("Account.DepositAsync(decimal, System.Threading.CancellationToken): 300 inputs", StringComparison.Ordinal));
        Assert.Contains(output, line => line.StartsWith("Account.Fee(decimal): 300 inputs", StringComparison.Ordinal));
    }

    [Fact]
    public void The_synchronous_form_from_the_design_works()
    {
        PledgeProperties.Check<Account>(iterations: 200);
    }

    [Fact]
    public async Task Declared_exceptions_whose_EnsuresOnThrow_holds_are_not_failures()
    {
        var frozen = new PledgePropertyOptions { Iterations = 200 }.Use(Gen.Select(Gen.Decimal[1m, 1_000m], balance => new Account(balance) { Frozen = true }));
        List<string> output = [];
        frozen.Output = output.Add;

        await PledgeProperties.CheckAsync(Member<Account>("Withdraw(decimal)"), frozen);

        var summary = Assert.Single(output, line => line.StartsWith("Account.Withdraw", StringComparison.Ordinal));
        Assert.Contains(" 0 kept every contract, ", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Records_enums_collections_and_nullable_inputs_are_generated()
    {
        List<string> output = [];

        await PledgeProperties.CheckAsync<Ledger>(new PledgePropertyOptions { Output = output.Add });

        Assert.Contains(output, line => line.StartsWith("Ledger.Record(PledgeContracts.Testing.Tests.Domain.Transfer): 100 inputs", StringComparison.Ordinal));
        Assert.Contains(output, line => line.StartsWith("Ledger.Total(System.Collections.Generic.IReadOnlyList<int>): 100 inputs", StringComparison.Ordinal));
        Assert.Contains(output, line => line.StartsWith("Ledger.Clamp(int?, System.Collections.Generic.List<string>): 100 inputs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Members_that_cannot_be_probed_are_skipped_in_a_type_check_and_refused_alone()
    {
        List<string> output = [];

        await PledgeProperties.CheckAsync<Ledger>(new PledgePropertyOptions { Output = output.Add });

        Assert.Contains("Ledger.First(System.ReadOnlySpan<int>): skipped, parameter 'values' is a ReadOnlySpan<int>, which cannot be passed as an object", output);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => PledgeProperties.CheckAsync(Member<Ledger>("First(System.ReadOnlySpan<int>)")));
        Assert.Contains("cannot be property-tested", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_undeclared_exception_is_a_counterexample_unless_allowed()
    {
        var parse = Member(typeof(Edges), "Parse(string)");

        var exception = await Assert.ThrowsAsync<PledgePropertyException>(() => PledgeProperties.CheckAsync(parse));
        Assert.Equal(PledgeOutcomeKind.Threw, exception.Counterexample!.Outcome.Kind);
        Assert.IsType<FormatException>(exception.InnerException);
        Assert.Contains("It threw FormatException, which no EnsuresOnThrow declares", exception.Message, StringComparison.Ordinal);

        await PledgeProperties.CheckAsync(parse, new PledgePropertyOptions { AllowUndeclaredExceptions = true });
    }

    [Fact]
    public async Task A_run_whose_every_input_was_rejected_fails_and_points_at_Use()
    {
        var exactly = Member(typeof(Edges), "Exactly(int)");

        var exception = await Assert.ThrowsAsync<PledgePropertyException>(() => PledgeProperties.CheckAsync(exactly));
        Assert.Null(exception.Counterexample);
        Assert.Contains("the preconditions rejected all 100 generated inputs", exception.Message, StringComparison.Ordinal);

        await PledgeProperties.CheckAsync(exactly, new PledgePropertyOptions().Use(Gen.Int[424_240, 424_245]));
    }

    [Fact]
    public async Task A_type_with_no_generator_is_refused_with_the_override_to_use()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => PledgeProperties.CheckAsync(Member(typeof(Edges), "Stamp(PledgeContracts.Testing.Tests.Domain.IClock)")));

        Assert.Contains("cannot generate IClock", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PledgePropertyOptions.Use<T>()", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failures_across_members_are_reported_together()
    {
        var exception = await Assert.ThrowsAsync<PledgePropertyException>(() => PledgeProperties.CheckAsync(typeof(Edges)));

        Assert.StartsWith("3 members of Edges broke their pledges.", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, Assert.IsType<AggregateException>(exception.InnerException).InnerExceptions.Count);
    }

    [Fact]
    public void A_type_without_registered_members_says_how_to_register_them()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PledgeProperties.Members<PledgePropertiesTests>());

        Assert.Contains("<PledgeRegistry>true</PledgeRegistry>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Options_are_validated()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PledgeProperties.CheckAsync<Account>(new PledgePropertyOptions { Iterations = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PledgeProperties.CheckAsync<Account>(new PledgePropertyOptions { Threads = 0 }));
    }

    private static PledgedMember Member<T>(string signature) => Member(typeof(T), signature);

    private static PledgedMember Member(Type type, string signature) =>
        Assert.Single(PledgeProperties.Members(type), member => member.Signature == signature);
}
