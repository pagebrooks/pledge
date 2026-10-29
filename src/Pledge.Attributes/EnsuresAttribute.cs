namespace PledgeContracts;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
public sealed class EnsuresAttribute(string expression) : ContractMetadataAttribute(expression)
{
}
