using System.Reflection;

namespace PledgeContracts.Testing.Tests;

public sealed class NamespaceTests
{
    // Same rule as PledgeContracts.Tests.NamespaceTests: test projects reference these assemblies next to
    // Pledge itself, so a namespace rooted at Pledge here would break `Pledge.Requires(...)` in every test.
    [Fact]
    public void No_shipped_namespace_is_rooted_at_Pledge()
    {
        Assembly[] shipped = [typeof(PledgeProperties).Assembly, typeof(global::PledgeContracts.Testing.Xunit.PledgeCase).Assembly];

        var offenders = shipped
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace is "Pledge" || type.Namespace?.StartsWith("Pledge.", StringComparison.Ordinal) is true)
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }
}
