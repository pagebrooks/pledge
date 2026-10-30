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
    // Surfaced to the generator by <CompilerVisibleProperty /> items in build/Pledge.targets, which also rejects
    // mode values outside the set at build time. Unknown values enforce rather than silently ignore.
    public const string ModeProperty = "build_property.PledgeMode";

    // Metadata attributes are documentation, not checks, so they have their own switch and default to on in every mode.
    public const string MetadataProperty = "build_property.PledgeMetadata";

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

    public static bool ParseMetadata(string? value) => !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    // The PledgeRuntime member generated checks call when a contract fails.
    public static string ReportMethod(PledgeMode mode) => mode switch
    {
        PledgeMode.Observe => "Observe",
        PledgeMode.EnforceFailFast => "EnforceFailFast",
        _ => "Enforce",
    };
}
