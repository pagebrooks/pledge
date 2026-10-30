using System.Reflection;

namespace PledgeContracts.Roslyn.Tests;

public sealed class MetadataGenerationTests
{
    private const string Source = """
        using System;
        using PledgeContracts;

        namespace Bank
        {
            public sealed class Account
            {
                public decimal Balance { get; private set; }

                public decimal Withdraw(decimal amount)
                {
                    Pledge.Requires(amount > 0);
                    Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
                    Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));
                    Balance -= amount;
                    return Balance;
                }

                public void Withdraw(decimal amount, string memo)
                {
                    Pledge.Requires(memo.Length > 0);
                    Balance -= amount;
                }
            }
        }
        """;

    [Fact]
    public void Contracts_are_recorded_as_assembly_level_attributes_naming_the_member()
    {
        var run = GeneratorHarness.Run(Source);

        var metadata = run.Metadata;
        Assert.NotNull(metadata);
        Assert.Contains("[assembly: global::PledgeContracts.Requires(typeof(global::Bank.Account), \"Withdraw(decimal)\", \"amount > 0\")]", metadata, StringComparison.Ordinal);
        Assert.Contains("[assembly: global::PledgeContracts.Ensures(typeof(global::Bank.Account), \"Withdraw(decimal)\", \"Balance == Old(Balance) - amount\")]", metadata, StringComparison.Ordinal);
        Assert.Contains("[assembly: global::PledgeContracts.EnsuresOnThrow(typeof(global::Bank.Account), \"Withdraw(decimal)\", typeof(global::System.InvalidOperationException), \"Balance == Old(Balance)\")]", metadata, StringComparison.Ordinal);
        Assert.Contains("[assembly: global::PledgeContracts.Requires(typeof(global::Bank.Account), \"Withdraw(decimal, string)\", \"memo.Length > 0\")]", metadata, StringComparison.Ordinal);

        var assembly = run.Load();
        var requires = assembly.GetCustomAttributes<RequiresAttribute>().ToList();
        Assert.Equal(2, requires.Count);
        Assert.All(requires, attribute => Assert.Equal("Bank.Account", attribute.DeclaringType!.FullName));
        var onThrow = Assert.Single(assembly.GetCustomAttributes<EnsuresOnThrowAttribute>());
        Assert.Equal(typeof(InvalidOperationException), onThrow.ExceptionType);
        Assert.Equal("Withdraw(decimal)", onThrow.Member);
    }

    [Fact]
    public void Metadata_is_emitted_in_Ignore_mode_and_without_call_sites()
    {
        var run = GeneratorHarness.Run(Source, mode: "Ignore");

        Assert.Empty(run.Wrappers);
        Assert.NotNull(run.Metadata);
        Assert.Equal(3, run.Load().GetCustomAttributes<ContractMetadataAttribute>().Count(attribute => attribute.Member == "Withdraw(decimal)"));
    }

    [Fact]
    public void Metadata_can_be_switched_off()
    {
        var run = GeneratorHarness.Run(Source, metadata: "false");

        Assert.Null(run.Metadata);
        Assert.Empty(run.Load().GetCustomAttributes<ContractMetadataAttribute>());
    }

    [Fact]
    public void Members_with_problems_get_no_metadata()
    {
        var run = GeneratorHarness.Run("""
            using PledgeContracts;

            public sealed class Holder
            {
                private int _limit = 5;

                public int M(int x) { Pledge.Requires(x < _limit); return x; }
            }
            """);

        Assert.Null(run.Metadata);
        Assert.Single(run.GeneratorDiagnostics);
    }
}
