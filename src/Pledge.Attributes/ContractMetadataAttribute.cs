namespace PledgeContracts;

// The declarative view of a contract. The generator writes these from the lifted predicates so other assemblies,
// documentation tools and analyzers can read a member's contracts without running them. The expression text is
// output, never input: nothing in the system parses it back.
//
// Two shapes: on the member itself, for code that declares its own members (generated decorators, hand-written
// metadata), and at assembly level naming the member by type and signature, which is the only shape a source
// generator can produce for members it did not declare.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public abstract class ContractMetadataAttribute : Attribute
{
    protected ContractMetadataAttribute(string expression)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    protected ContractMetadataAttribute(Type declaringType, string member, string expression)
        : this(expression)
    {
        DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
        Member = member ?? throw new ArgumentNullException(nameof(member));
    }

    public string Expression { get; }

    // Set on the assembly-level shape: the type that declares the contracted member.
    public Type? DeclaringType { get; }

    // Set on the assembly-level shape: the member's name and parameter types, e.g. "Withdraw(decimal)".
    public string? Member { get; }
}
