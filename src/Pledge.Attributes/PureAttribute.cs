namespace PledgeContracts;

// Marks a member as side-effect free so contract expressions may call it. Interchangeable with
// System.Diagnostics.Contracts.PureAttribute, which the purity analyzer accepts as well; this one exists
// so libraries can mark purity without importing the Code Contracts namespace. Same targets as the BCL's.
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property |
    AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = true)]
public sealed class PureAttribute : Attribute
{
}
