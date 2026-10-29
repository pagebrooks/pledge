namespace PledgeContracts.Roslyn.Generator;

internal enum PledgeMode
{
    Ignore,
    Observe,
    Enforce,
    EnforceFailFast,
}

internal static class PledgeModes
{
    // Surfaced to the generator by <CompilerVisibleProperty Include="PledgeMode" /> in build/Pledge.targets,
    // which also rejects values outside this set at build time. Unknown values enforce rather than silently ignore.
    public const string Property = "build_property.PledgeMode";

    public static PledgeMode Parse(string? value)
    {
        if (string.Equals(value, "Ignore", StringComparison.OrdinalIgnoreCase))
        {
            return PledgeMode.Ignore;
        }

        if (string.Equals(value, "Observe", StringComparison.OrdinalIgnoreCase))
        {
            return PledgeMode.Observe;
        }

        if (string.Equals(value, "EnforceFailFast", StringComparison.OrdinalIgnoreCase))
        {
            return PledgeMode.EnforceFailFast;
        }

        return PledgeMode.Enforce;
    }

    // The PledgeRuntime member generated checks call when a contract fails.
    public static string ReportMethod(PledgeMode mode) => mode switch
    {
        PledgeMode.Observe => "Observe",
        PledgeMode.EnforceFailFast => "EnforceFailFast",
        _ => "Enforce",
    };
}
