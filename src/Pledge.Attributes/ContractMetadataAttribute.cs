namespace PledgeContracts;

// The declarative view of a contract. The generator writes these from the lifted predicates so other
// assemblies, documentation tools and analyzers can read a member's contracts without running them.
// The expression text is output, never input: nothing in the system parses it back.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
public abstract class ContractMetadataAttribute : Attribute
{
    protected ContractMetadataAttribute(string expression)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public string Expression { get; }
}
