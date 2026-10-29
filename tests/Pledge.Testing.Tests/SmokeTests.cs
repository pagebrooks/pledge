namespace PledgeContracts.Testing.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Testing_bridge_assemblies_are_referenced()
    {
        Assert.Equal("Pledge.Testing", typeof(Placeholder).Assembly.GetName().Name);
        Assert.Equal("Pledge.Testing.Xunit", typeof(global::PledgeContracts.Testing.Xunit.Placeholder).Assembly.GetName().Name);
    }
}
