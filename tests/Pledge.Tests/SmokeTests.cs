namespace PledgeContracts.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Runtime_assembly_is_referenced() =>
        Assert.Equal("Pledge", typeof(Pledge).Assembly.GetName().Name);
}
