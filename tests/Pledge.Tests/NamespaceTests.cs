using System.Reflection;

namespace PledgeContracts.Tests;

public sealed class NamespaceTests
{
    // C# binds a simple name to a namespace member before it looks at using directives, so a namespace
    // rooted at Pledge in any referenced assembly would make `Pledge.Requires(...)` resolve `Pledge` to
    // the namespace and fail to compile. Every shipped namespace is rooted at PledgeContracts instead.
    [Fact]
    public void No_shipped_namespace_is_rooted_at_Pledge()
    {
        Assembly[] shipped = [typeof(Placeholder).Assembly, typeof(AttributesPlaceholder).Assembly];

        var offenders = shipped
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace is "Pledge" || type.Namespace?.StartsWith("Pledge.", StringComparison.Ordinal) is true)
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Attributes_share_the_runtime_namespace()
    {
        Assert.Equal("Pledge.Attributes", typeof(AttributesPlaceholder).Assembly.GetName().Name);
        Assert.Equal(typeof(Placeholder).Namespace, typeof(AttributesPlaceholder).Namespace);
    }
}
