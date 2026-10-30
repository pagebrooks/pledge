namespace PledgeContracts.Build.Tests;

// What Pledge.targets makes of a project's properties, read back from MSBuild after every import.
public sealed class ModePropertyTests
{
    private static readonly string[] _modeProperties = ["PledgeMode", "PledgePreconditions", "PledgePostconditions", "PledgeInvariants", "PledgeMetadata", "PledgeRegistry"];

    [Theory]
    [InlineData("Debug", "Enforce")]
    [InlineData("Release", "Ignore")]
    public async Task Defaults_follow_the_configuration(string configuration, string expected)
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App");

        var values = await fixture.EvaluateAsync("App", _modeProperties, "-p:Configuration=" + configuration);

        Assert.Equal(expected, values["PledgeMode"]);
        Assert.Equal(expected, values["PledgePreconditions"]);
        Assert.Equal(expected, values["PledgePostconditions"]);
        Assert.Equal(expected, values["PledgeInvariants"]);
        Assert.Equal("true", values["PledgeMetadata"]);
        Assert.Equal(expected == "Ignore" ? "false" : "true", values["PledgeRegistry"]);
    }

    [Theory]
    [InlineData("Ignore", null, null, "false")]
    [InlineData("Ignore", "Observe", null, "true")]
    [InlineData("Enforce", null, "false", "false")]
    [InlineData("Ignore", null, "true", "true")]
    public async Task The_registry_is_on_unless_every_kind_is_Ignore_or_it_is_set(string mode, string? postconditions, string? registry, string expected)
    {
        using var fixture = Fixture.Create();
        var properties = new Dictionary<string, string> { ["PledgeMode"] = mode };
        if (postconditions is not null)
        {
            properties["PledgePostconditions"] = postconditions;
        }

        if (registry is not null)
        {
            properties["PledgeRegistry"] = registry;
        }

        fixture.WriteProject("App", properties: properties);

        var values = await fixture.EvaluateAsync("App", _modeProperties);

        Assert.Equal(expected, values["PledgeRegistry"]);
    }

    [Fact]
    public async Task Per_kind_properties_default_to_PledgeMode_and_override_it()
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: new Dictionary<string, string>
        {
            ["PledgeMode"] = "Observe",
            ["PledgePostconditions"] = "Ignore",
        });

        var values = await fixture.EvaluateAsync("App", _modeProperties, "-p:Configuration=Release");

        Assert.Equal("Observe", values["PledgeMode"]);
        Assert.Equal("Observe", values["PledgePreconditions"]);
        Assert.Equal("Ignore", values["PledgePostconditions"]);
        Assert.Equal("Observe", values["PledgeInvariants"]);
    }

    [Theory]
    [InlineData(null, ";PledgeContracts.Generated")]
    [InlineData("My.Own", "My.Own;PledgeContracts.Generated")]
    [InlineData("My.Own;PledgeContracts.Generated", "My.Own;PledgeContracts.Generated")]
    public async Task The_generator_namespace_is_appended_once_after_the_project_body(string? projectValue, string expected)
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: projectValue is null ? null : new Dictionary<string, string> { ["InterceptorsNamespaces"] = projectValue });

        var values = await fixture.EvaluateAsync("App", ["InterceptorsNamespaces", "PledgeMode"]);

        Assert.Equal(expected, values["InterceptorsNamespaces"]);
    }

    [Theory]
    [InlineData("PledgeMode", "Bogus")]
    [InlineData("PledgePreconditions", "Enforced")]
    [InlineData("PledgePostconditions", "Off")]
    [InlineData("PledgeInvariants", "Assert")]
    public async Task An_unknown_mode_fails_the_build_naming_the_property(string property, string value)
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: new Dictionary<string, string> { [property] = value });

        var result = await fixture.RunTargetAsync("App", "PledgeValidateMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains($"{property} '{value}' is not recognized. Use Ignore, Observe, Enforce or EnforceFailFast.", result.Combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_metadata_switch_fails_the_build()
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: new Dictionary<string, string> { ["PledgeMetadata"] = "maybe" });

        var result = await fixture.RunTargetAsync("App", "PledgeValidateMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("PledgeMetadata 'maybe' is not recognized. Use true or false.", result.Combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_registry_switch_fails_the_build()
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: new Dictionary<string, string> { ["PledgeRegistry"] = "sometimes" });

        var result = await fixture.RunTargetAsync("App", "PledgeValidateMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("PledgeRegistry 'sometimes' is not recognized. Use true or false.", result.Combined, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PledgeMetadata", "yes")]
    [InlineData("PledgeMetadata", "off")]
    [InlineData("PledgeRegistry", "on")]
    [InlineData("PledgeRegistry", "no")]
    public async Task MSBuild_boolean_spellings_of_the_switches_are_accepted(string property, string value)
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: new Dictionary<string, string> { [property] = value });

        var result = await fixture.RunTargetAsync("App", "PledgeValidateMode");

        result.AssertSucceeded();
    }

    [Fact]
    public async Task Mode_values_are_case_insensitive()
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: new Dictionary<string, string>
        {
            ["PledgeMode"] = "enforce",
            ["PledgePostconditions"] = "OBSERVE",
            ["PledgeMetadata"] = "False",
        });

        var result = await fixture.RunTargetAsync("App", "PledgeValidateMode");

        result.AssertSucceeded();
    }

    [Fact]
    public async Task A_command_line_value_that_drops_the_namespace_fails_with_the_line_to_add()
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App");

        var result = await fixture.RunTargetAsync("App", "PledgeValidateInterceptors", "-p:PledgeMode=Enforce", "-p:InterceptorsNamespaces=Other.Generators");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("InterceptorsNamespaces does not contain PledgeContracts.Generated", result.Combined, StringComparison.Ordinal);
        Assert.Contains("<InterceptorsNamespaces>$(InterceptorsNamespaces);PledgeContracts.Generated</InterceptorsNamespaces>", result.Combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Directory_Build_targets_that_drops_the_namespace_fails_the_build()
    {
        // MSBuild imports Directory.Build.targets after package targets, so a plain assignment there overwrites the
        // append; the target is what catches it.
        using var fixture = Fixture.Create();
        fixture.WriteProject("App", properties: new Dictionary<string, string> { ["PledgeMode"] = "Enforce" });
        File.WriteAllText(Path.Combine(fixture.Directory, "Directory.Build.targets"), """
            <Project>
              <PropertyGroup>
                <InterceptorsNamespaces>Other.Generators</InterceptorsNamespaces>
              </PropertyGroup>
            </Project>
            """);

        var result = await fixture.RunTargetAsync("App", "PledgeValidateInterceptors");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("InterceptorsNamespaces does not contain PledgeContracts.Generated", result.Combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_namespace_check_is_skipped_when_every_kind_is_Ignore()
    {
        using var fixture = Fixture.Create();
        fixture.WriteProject("App");

        var result = await fixture.RunTargetAsync("App", "PledgeValidateInterceptors", "-p:PledgeMode=Ignore", "-p:InterceptorsNamespaces=Other.Generators");

        result.AssertSucceeded();
    }
}
