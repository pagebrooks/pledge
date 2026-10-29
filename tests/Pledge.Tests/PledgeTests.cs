using System.Diagnostics;
using System.Reflection;

namespace PledgeContracts.Tests;

public sealed class PledgeTests
{
    [Theory]
    [InlineData(nameof(Pledge.Requires))]
    [InlineData(nameof(Pledge.Ensures))]
    [InlineData(nameof(Pledge.EnsuresOnThrow))]
    public void Contract_calls_are_conditional_on_a_symbol_nobody_defines(string name)
    {
        var method = typeof(Pledge).GetMethod(name, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var conditional = Assert.Single(method.GetCustomAttributes<ConditionalAttribute>());
        Assert.Equal("PLEDGE_NEVER", conditional.ConditionString);
    }

    [Fact]
    public void Contract_calls_compile_away_with_their_arguments()
    {
        // Each argument would throw if evaluated; the compiler drops the whole call, so nothing runs.
        Pledge.Requires(Explode());
        Pledge.Requires(Explode(), "with a message");
        Pledge.Ensures(Explode());
        Pledge.EnsuresOnThrow<InvalidOperationException>(Explode());
    }

    [Fact]
    public void Result_and_Old_return_values_so_they_cannot_be_conditional()
    {
        Assert.Empty(typeof(Pledge).GetMethod(nameof(Pledge.Result))!.GetCustomAttributes<ConditionalAttribute>());
        Assert.Empty(typeof(Pledge).GetMethod(nameof(Pledge.Old))!.GetCustomAttributes<ConditionalAttribute>());
    }

    [Fact]
    public void Result_and_Old_throw_when_reached_outside_a_contract()
    {
        Assert.Throws<InvalidOperationException>(() => Pledge.Result<int>());
        Assert.Throws<InvalidOperationException>(() => Pledge.Old(42));
    }

    private static bool Explode() => throw new InvalidOperationException("A contract argument was evaluated at runtime.");
}
