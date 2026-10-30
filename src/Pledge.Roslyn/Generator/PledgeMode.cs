namespace PledgeContracts.Roslyn.Generator;

internal enum PledgeMode
{
    Ignore,
    Observe,
    Enforce,
    EnforceFailFast,
}

// The mode each kind of contract compiles in. PledgeMode sets all of them; PledgePreconditions and
// PledgePostconditions override it per kind. EnsuresOnThrow is a postcondition. Invariants have a property and
// validation in Pledge.targets already but nothing to check until v1.0, so they are not modelled here.
internal readonly record struct ModeSettings(PledgeMode Preconditions, PledgeMode Postconditions)
{
    public PledgeMode For(ContractKind kind) => kind == ContractKind.Precondition ? Preconditions : Postconditions;
}

internal static class PledgeModes
{
    // Surfaced to the generator by <CompilerVisibleProperty /> items in build/Pledge.targets, which also rejects
    // mode values outside the set at build time and defaults the per-kind properties to PledgeMode. The generator
    // repeats that fallback so it behaves the same without the targets. Unknown values enforce rather than ignore.
    public const string ModeProperty = "build_property.PledgeMode";
    public const string PreconditionsProperty = "build_property.PledgePreconditions";
    public const string PostconditionsProperty = "build_property.PledgePostconditions";

    // Metadata attributes are documentation, not checks, so they have their own switch and default to on in every mode.
    public const string MetadataProperty = "build_property.PledgeMetadata";

    public static ModeSettings Parse(string? mode, string? preconditions, string? postconditions)
    {
        var overall = Parse(mode);
        return new ModeSettings(
            string.IsNullOrEmpty(preconditions) ? overall : Parse(preconditions),
            string.IsNullOrEmpty(postconditions) ? overall : Parse(postconditions));
    }

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

    // MSBuild conditions treat no and off as false (and yes and on as true), so Pledge.targets' validation accepts
    // them; the generator must read them the same way.
    public static bool ParseMetadata(string? value) =>
        !(string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase));

    // The PledgeRuntime member generated checks call when a contract fails.
    public static string ReportMethod(PledgeMode mode) => mode switch
    {
        PledgeMode.Observe => "Observe",
        PledgeMode.EnforceFailFast => "EnforceFailFast",
        _ => "Enforce",
    };
}
