using Microsoft.CodeAnalysis;

namespace Pledge.Generator.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Generator_is_an_incremental_generator() =>
        Assert.IsAssignableFrom<IIncrementalGenerator>(new PledgeGenerator());
}
