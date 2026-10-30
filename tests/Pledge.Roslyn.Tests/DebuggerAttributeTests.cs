using System.Diagnostics;
using System.Reflection;

namespace PledgeContracts.Roslyn.Tests;

// F11 on a contracted call should land in the user's method: every generated method that sits between a caller and
// the original is [DebuggerStepThrough]. Only the module initializer, which nobody steps into, is left alone.
public sealed class DebuggerAttributeTests
{
    private static readonly string[] _generatedMethods = ["__Intercept0", "__Checked", "__Awaited", "Probe", "Run", "Violation"];

    private const string Source = """
        using System.Threading.Tasks;
        using PledgeContracts;

        namespace Bank
        {
            public sealed class Account
            {
                public decimal Balance { get; private set; }

                public decimal Deposit(decimal amount)
                {
                    Pledge.Requires(amount > 0);
                    Pledge.Ensures(Balance == Pledge.Old(Balance) + amount);
                    Balance += amount;
                    return Balance;
                }

                public async Task<decimal> DepositAsync(decimal amount)
                {
                    Pledge.Requires(amount > 0);
                    Pledge.Ensures(Pledge.Result<decimal>() == Balance);
                    await Task.Yield();
                    Balance += amount;
                    return Balance;
                }
            }
        }

        public static class Scenario
        {
            public static async Task<decimal> Run()
            {
                var account = new Bank.Account();
                account.Deposit(1m);
                return await account.DepositAsync(2m);
            }
        }
        """;

    [Fact]
    public void Wrappers_and_probes_are_stepped_through()
    {
        var run = GeneratorHarness.Run(Source);
        run.AssertCompiles();

        var methods = run.Load().GetTypes()
            .Where(type => type.Namespace == "PledgeContracts.Generated")
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.Name != "Register")
            .ToList();

        Assert.Equivalent(_generatedMethods, methods.Select(method => method.Name).Distinct().ToArray());
        Assert.All(
            methods,
            method => Assert.True(method.IsDefined(typeof(DebuggerStepThroughAttribute)), method.DeclaringType!.Name + "." + method.Name));
    }
}
