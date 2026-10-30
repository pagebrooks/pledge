namespace PledgeContracts;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EnsuresAttribute : ContractMetadataAttribute
{
    public EnsuresAttribute(string expression)
        : base(expression)
    {
    }

    public EnsuresAttribute(Type declaringType, string member, string expression)
        : base(declaringType, member, expression)
    {
    }
}
