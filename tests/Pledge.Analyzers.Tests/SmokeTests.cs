namespace Pledge.Analyzers.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Analyzer_assemblies_are_referenced()
    {
        Assert.Equal("Pledge.Analyzers", typeof(Placeholder).Assembly.GetName().Name);
        Assert.Equal("Pledge.Analyzers.CodeFixes", typeof(CodeFixes.Placeholder).Assembly.GetName().Name);
    }
}
