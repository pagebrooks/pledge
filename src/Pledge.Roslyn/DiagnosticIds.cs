namespace PledgeContracts.Roslyn;

// The PLEDGE diagnostic ids, shared with Pledge.Roslyn.CodeFixes as a linked file so the two assemblies agree
// without referencing each other. The table in DESIGN.md is the source of truth for meaning and severity.
internal static class DiagnosticIds
{
    public const string ContractNotAtTop = "PLEDGE001";
    public const string ResultMisused = "PLEDGE002";
    public const string OldMisused = "PLEDGE003";
    public const string ImpureContract = "PLEDGE004";
    public const string GeneratorNotActive = "PLEDGE005";
    public const string NoCallSites = "PLEDGE006";
    public const string OldOfMutableReference = "PLEDGE007";
    public const string ConstantViolatesPrecondition = "PLEDGE008";
    public const string UnsupportedTarget = "PLEDGE009";
}
