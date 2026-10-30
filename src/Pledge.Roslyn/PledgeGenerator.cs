using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using PledgeContracts.Roslyn.Generator;

namespace PledgeContracts.Roslyn;

[Generator(LanguageNames.CSharp)]
public sealed class PledgeGenerator : IIncrementalGenerator
{
    public const string InterceptorsFeature = "InterceptorsNamespaces";

    public static class TrackingNames
    {
        public const string Methods = "Methods";
        public const string CallSites = "CallSites";
        public const string Misplaced = "Misplaced";
        public const string Probes = "Probes";
        public const string Wrappers = "Wrappers";
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => ContractSyntax.IsCandidateDeclaration(node),
                static (syntaxContext, cancellationToken) => ContractParser.Parse((MethodDeclarationSyntax)syntaxContext.Node, syntaxContext.SemanticModel, cancellationToken))
            .Where(static method => method is not null)
            .Select(static (method, _) => method!)
            .WithTrackingName(TrackingNames.Methods);

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax invocation && !ContractSyntax.IsPledgeCall(invocation),
                static (syntaxContext, cancellationToken) => CallSiteParser.Parse((InvocationExpressionSyntax)syntaxContext.Node, syntaxContext.SemanticModel, cancellationToken))
            .Where(static site => site is not null)
            .Select(static (site, _) => site!.Value)
            .WithTrackingName(TrackingNames.CallSites);

        var misplaced = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => ContractSyntax.IsContractInvocation(node),
                static (syntaxContext, cancellationToken) => MisplacedContractParser.Parse((InvocationExpressionSyntax)syntaxContext.Node, syntaxContext.SemanticModel, cancellationToken))
            .Where(static contract => contract is not null)
            .Select(static (contract, _) => contract!.Value)
            .WithTrackingName(TrackingNames.Misplaced);

        var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => (
            Modes: PledgeModes.Parse(Option(provider, PledgeModes.ModeProperty), Option(provider, PledgeModes.PreconditionsProperty), Option(provider, PledgeModes.PostconditionsProperty)),
            Metadata: PledgeModes.ParseSwitch(Option(provider, PledgeModes.MetadataProperty)),
            Registry: Option(provider, PledgeModes.RegistryProperty)));

        var interceptorsEnabled = context.ParseOptionsProvider.Select(static (parseOptions, _) => InterceptorsEnabled(parseOptions));

        context.RegisterSourceOutput(methods, static (productionContext, method) => ReportProblems(productionContext, method));
        context.RegisterSourceOutput(misplaced, static (productionContext, contract) =>
            productionContext.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedTarget, contract.Site.ToLocation(), contract.Member, contract.Problem)));

        // Probes are per method, so editing one contracted method regenerates only its probe.
        context.RegisterSourceOutput(methods.Combine(options).WithTrackingName(TrackingNames.Probes), static (productionContext, input) =>
        {
            var (method, settings) = input;
            if (method.IsGenerable && PledgeModes.ParseRegistry(settings.Registry, settings.Modes))
            {
                productionContext.AddSource(ProbeEmitter.HintName(method), ProbeEmitter.Emit(method));
            }
        });

        var collected = methods.Collect();
        context.RegisterSourceOutput(collected.Combine(options), static (productionContext, input) => EmitMetadata(productionContext, input.Left, input.Right.Metadata));

        var settings = options.Select(static (options, _) => options.Modes).Combine(interceptorsEnabled);
        context.RegisterSourceOutput(collected.Combine(settings), static (productionContext, input) =>
            ReportInactive(productionContext, input.Left, input.Right.Left, input.Right.Right));

        // Wrappers are per method too: each one is paired with its own call sites, so an edit regenerates only the
        // wrappers whose method or call sites changed. Handing every method and site to one output instead made each
        // keystroke re-emit, and the driver re-parse, every wrapper in the project.
        var sitesByMethod = callSites.Collect().Select(static (sites, _) => GroupByMethod(sites));
        var wrappers = methods
            .Combine(sitesByMethod)
            .Select(static (input, _) => (Method: input.Left, Sites: input.Right.TryGetValue(input.Left.Key, out var sites) ? sites : EquatableArray<CallSite>.Empty))
            .Combine(settings)
            .WithTrackingName(TrackingNames.Wrappers);
        context.RegisterSourceOutput(wrappers, static (productionContext, input) =>
            EmitWrapper(productionContext, input.Left.Method, input.Left.Sites, input.Right.Left, input.Right.Right));
    }

    private static string? Option(AnalyzerConfigOptionsProvider provider, string key) =>
        provider.GlobalOptions.TryGetValue(key, out var value) ? value : null;

    private static bool InterceptorsEnabled(ParseOptions options)
    {
        if (!options.Features.TryGetValue(InterceptorsFeature, out var namespaces))
        {
            return false;
        }

        foreach (var ns in namespaces.Split(';'))
        {
            if (ns.Trim() == WrapperEmitter.Namespace)
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportProblems(SourceProductionContext context, ContractedMethod method)
    {
        foreach (var problem in method.Problems)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedTarget, method.Site.ToLocation(), method.MethodName, problem));
        }
    }

    private static void EmitMetadata(SourceProductionContext context, ImmutableArray<ContractedMethod> methods, bool enabled)
    {
        if (!enabled || methods.IsEmpty)
        {
            return;
        }

        if (MetadataEmitter.Emit(methods) is { } source)
        {
            context.AddSource(MetadataEmitter.HintName, source);
        }
    }

    private static ImmutableDictionary<string, EquatableArray<CallSite>> GroupByMethod(ImmutableArray<CallSite> sites) =>
        sites
            .GroupBy(site => site.MethodKey, StringComparer.Ordinal)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.OrderBy(site => site.FilePath, StringComparer.Ordinal).ThenBy(site => site.Line).ToEquatableArray(),
                StringComparer.Ordinal);

    // Only what the modes leave enabled is generated; a method whose every contract kind is ignored is not checked
    // anywhere, so it gets neither a wrapper nor PLEDGE005 or PLEDGE006.
    private static ContractedMethod? Enabled(ContractedMethod method, ModeSettings modes) =>
        method.IsGenerable ? method.Enabled(modes) : null;

    // PLEDGE005 once per project, at the first method that would have had checks.
    private static void ReportInactive(SourceProductionContext context, ImmutableArray<ContractedMethod> methods, ModeSettings modes, bool interceptorsEnabled)
    {
        if (interceptorsEnabled)
        {
            return;
        }

        foreach (var method in methods)
        {
            if (Enabled(method, modes) is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.GeneratorNotActive, method.Site.ToLocation(), WrapperEmitter.Namespace));
                return;
            }
        }
    }

    private static void EmitWrapper(SourceProductionContext context, ContractedMethod method, EquatableArray<CallSite> sites, ModeSettings modes, bool interceptorsEnabled)
    {
        if (!interceptorsEnabled || Enabled(method, modes) is not { } enabled)
        {
            return;
        }

        if (sites.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoCallSites, method.Site.ToLocation(), method.MethodName));
            return;
        }

        context.AddSource(method.HintName, WrapperEmitter.Emit(enabled, sites, modes));
    }
}
