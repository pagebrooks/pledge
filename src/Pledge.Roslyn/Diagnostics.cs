using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn;

// Every PLEDGE diagnostic the generator and the analyzers report. Ids and severities follow the table in DESIGN.md.
internal static class Diagnostics
{
    private const string Category = "Pledge";

    public static readonly DiagnosticDescriptor GeneratorNotActive = new(
        id: "PLEDGE005",
        title: "Pledge generator is not active",
        messageFormat: "Contracts in this project compile to no-ops: the interceptors feature is not enabled for namespace '{0}'. Reference the Pledge package so its build props set InterceptorsNamespaces, or add the namespace to that property yourself.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedTarget = new(
        id: "PLEDGE009",
        title: "Contract on an unsupported member",
        messageFormat: "Contracts on '{0}' compile to no-ops: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
