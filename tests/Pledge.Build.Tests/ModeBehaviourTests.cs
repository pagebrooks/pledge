namespace PledgeContracts.Build.Tests;

// Builds the Modes fixture app under each mode and runs it. The app breaks a precondition (a negative deposit)
// and then a postcondition (a leaky withdrawal) and prints what happened; see Fixtures/Modes/Program.cs.
public sealed class ModeBehaviourTests
{
    [Fact]
    public async Task Enforce_reports_to_the_handler_then_throws_the_kind_specific_exception()
    {
        var result = await RunAsync("Release", new Dictionary<string, string> { ["PledgeMode"] = "Enforce" });

        result.AssertSucceeded();
        AssertLines(result,
            "handler: Precondition amount > 0",
            "deposit: threw PreconditionViolationException",
            "handler: Postcondition Balance == Old(Balance) - amount",
            "withdraw: threw PostconditionViolationException",
            "done");
    }

    [Fact]
    public async Task Observe_reports_to_the_handler_and_carries_on()
    {
        var result = await RunAsync("Release", new Dictionary<string, string> { ["PledgeMode"] = "Observe" });

        result.AssertSucceeded();
        AssertLines(result,
            "handler: Precondition amount > 0",
            "deposit: returned 9",
            "handler: Postcondition Balance == Old(Balance) - amount",
            "withdraw: returned 5",
            "done");
    }

    [Fact]
    public async Task Ignore_checks_nothing()
    {
        var result = await RunAsync("Release", new Dictionary<string, string> { ["PledgeMode"] = "Ignore" });

        result.AssertSucceeded();
        AssertLines(result,
            "deposit: returned 9",
            "withdraw: returned 5",
            "done");
    }

    [Fact]
    public async Task EnforceFailFast_reports_to_the_handler_then_terminates_the_process()
    {
        var result = await RunAsync("Release", new Dictionary<string, string> { ["PledgeMode"] = "EnforceFailFast" });

        Assert.NotEqual(0, result.ExitCode);
        AssertLines(result, "handler: Precondition amount > 0");
        Assert.Contains("Precondition of Account.Deposit violated by its caller: amount > 0", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_kind_runs_in_its_own_mode()
    {
        var result = await RunAsync("Release", new Dictionary<string, string>
        {
            ["PledgeMode"] = "Enforce",
            ["PledgePreconditions"] = "Ignore",
            ["PledgePostconditions"] = "Observe",
        });

        result.AssertSucceeded();
        AssertLines(result,
            "deposit: returned 9",
            "handler: Postcondition Balance == Old(Balance) - amount",
            "withdraw: returned 5",
            "done");
    }

    [Theory]
    [InlineData("Debug", true)]
    [InlineData("Release", false)]
    public async Task Without_a_mode_Debug_enforces_and_Release_ignores(string configuration, bool enforced)
    {
        var result = await RunAsync(configuration, properties: null);

        result.AssertSucceeded();
        Assert.Equal(enforced, result.Output.Contains("deposit: threw PreconditionViolationException", StringComparison.Ordinal));
        Assert.Equal(!enforced, result.Output.Contains("deposit: returned 9", StringComparison.Ordinal));
    }

    private static async Task<ProcessResult> RunAsync(string configuration, IReadOnlyDictionary<string, string>? properties)
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", outputType: "Exe", properties: properties);
        fixture.CopySources("App", "Modes");

        var build = await fixture.BuildAsync("App", configuration);
        build.AssertSucceeded();

        return await fixture.DotnetAsync(fixture.OutputPath("App", configuration));
    }

    private static void AssertLines(ProcessResult result, params string[] expected)
    {
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(expected, lines);
    }
}
