using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn;

// Every PLEDGE diagnostic the generator and the analyzers report. Ids and severities follow the table in DESIGN.md.
// PLEDGE002 has one descriptor per way to misuse Result<T>() so each message says exactly what is wrong.
internal static class Diagnostics
{
    private const string Category = "Pledge";
    private const string HelpBase = "https://github.com/pagebrooks/pledge/blob/main/docs/rules/";

    public static readonly DiagnosticDescriptor ContractNotAtTop = new(
        id: DiagnosticIds.ContractNotAtTop,
        title: "Contract call is not at the top of the method",
        messageFormat: "Pledge.{0} is a contract only at the top of the method body, before any other statement; here it compiles to nothing",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ContractNotAtTop + ".md");

    public static readonly DiagnosticDescriptor ResultOutsideEnsures = new(
        id: DiagnosticIds.ResultMisused,
        title: "Result<T>() used outside Pledge.Ensures",
        messageFormat: "Pledge.Result<T>() is only meaningful inside Pledge.Ensures",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ResultMisused + ".md");

    public static readonly DiagnosticDescriptor ResultOnThrow = new(
        id: DiagnosticIds.ResultMisused,
        title: "Result<T>() used in Pledge.EnsuresOnThrow",
        messageFormat: "Pledge.Result<T>() has no value when the member throws, so it cannot appear in Pledge.EnsuresOnThrow",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ResultMisused + ".md");

    public static readonly DiagnosticDescriptor ResultInOld = new(
        id: DiagnosticIds.ResultMisused,
        title: "Result<T>() used inside Pledge.Old",
        messageFormat: "Pledge.Result<T>() cannot appear inside Pledge.Old(...): there is no result yet when the snapshot is taken",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ResultMisused + ".md");

    public static readonly DiagnosticDescriptor ResultInVoidMethod = new(
        id: DiagnosticIds.ResultMisused,
        title: "Result<T>() used in a method that returns nothing",
        messageFormat: "Pledge.Result<T>() cannot be used in '{0}', which returns nothing",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ResultMisused + ".md");

    public static readonly DiagnosticDescriptor ResultTypeMismatch = new(
        id: DiagnosticIds.ResultMisused,
        title: "Result<T>() names the wrong type",
        messageFormat: "Pledge.Result<T>() must name the result type of '{0}': Result<{1}>()",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ResultMisused + ".md");

    public static readonly DiagnosticDescriptor OldOutsidePostcondition = new(
        id: DiagnosticIds.OldMisused,
        title: "Old(...) used outside a postcondition",
        messageFormat: "Pledge.Old(...) is only meaningful inside Pledge.Ensures or Pledge.EnsuresOnThrow, where there is a 'before' to compare against",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.OldMisused + ".md");

    public static readonly DiagnosticDescriptor ImpureContract = new(
        id: DiagnosticIds.ImpureContract,
        title: "Impure expression in a contract",
        messageFormat: "Contract expression is not pure: it {0}; contracts run in some build modes and not others, so a side effect here changes behaviour between them",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ImpureContract + ".md");

    public static readonly DiagnosticDescriptor GeneratorNotActive = new(
        id: DiagnosticIds.GeneratorNotActive,
        title: "Pledge generator is not active",
        messageFormat: "Contracts in this project compile to no-ops: the interceptors feature is not enabled for namespace '{0}'. Reference the Pledge package so its build props set InterceptorsNamespaces, or add the namespace to that property yourself.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.GeneratorNotActive + ".md");

    public static readonly DiagnosticDescriptor NoCallSites = new(
        id: DiagnosticIds.NoCallSites,
        title: "Contracted method has no call sites in this compilation",
        messageFormat: "'{0}' is not called from this compilation, so its contracts are never checked here; interceptor mode checks a method where it is called, and callers in other assemblies are not intercepted",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.NoCallSites + ".md");

    public static readonly DiagnosticDescriptor OldOfMutableReference = new(
        id: DiagnosticIds.OldOfMutableReference,
        title: "Old(...) of a mutable reference",
        messageFormat: "Pledge.Old({0}) snapshots a reference to the mutable type '{1}', not its contents, so a change made by the body shows in the snapshot too; copy it first, for example with .ToArray(), a with expression or a constructor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.OldOfMutableReference + ".md");

    public static readonly DiagnosticDescriptor ConstantViolatesPrecondition = new(
        id: DiagnosticIds.ConstantViolatesPrecondition,
        title: "Constant argument violates a precondition",
        messageFormat: "Argument {0} violates the precondition '{1}' of '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.ConstantViolatesPrecondition + ".md");

    public static readonly DiagnosticDescriptor UnsupportedTarget = new(
        id: DiagnosticIds.UnsupportedTarget,
        title: "Contract on an unsupported member",
        messageFormat: "Contracts on '{0}' compile to no-ops: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + DiagnosticIds.UnsupportedTarget + ".md");
}
