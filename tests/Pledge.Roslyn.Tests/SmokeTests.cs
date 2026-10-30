using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Generator_is_an_incremental_generator() =>
        Assert.IsAssignableFrom<IIncrementalGenerator>(new PledgeGenerator());

    [Fact]
    public void Code_fix_assembly_is_referenced() =>
        Assert.Equal("Pledge.Roslyn.CodeFixes", typeof(CodeFixes.MoveContractToTopCodeFixProvider).Assembly.GetName().Name);
}
