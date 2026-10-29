namespace Pledge.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Runtime_assembly_is_referenced() =>
        Assert.Equal("Pledge", typeof(Placeholder).Assembly.GetName().Name);
}
