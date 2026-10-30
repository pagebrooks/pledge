namespace PledgeContracts;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class RequiresAttribute : ContractMetadataAttribute
{
    public RequiresAttribute(string expression)
        : base(expression)
    {
    }

    public RequiresAttribute(Type declaringType, string member, string expression)
        : base(declaringType, member, expression)
    {
    }
}
