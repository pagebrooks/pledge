namespace PledgeContracts;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
public sealed class RequiresAttribute(string expression) : ContractMetadataAttribute(expression)
{
}
