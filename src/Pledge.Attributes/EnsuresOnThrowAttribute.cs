namespace PledgeContracts;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
public sealed class EnsuresOnThrowAttribute : ContractMetadataAttribute
{
    public EnsuresOnThrowAttribute(Type exceptionType, string expression)
        : base(expression)
    {
        ExceptionType = exceptionType ?? throw new ArgumentNullException(nameof(exceptionType));
    }

    public Type ExceptionType { get; }
}
