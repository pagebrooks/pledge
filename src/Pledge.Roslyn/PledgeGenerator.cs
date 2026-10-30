using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
                static (node, _) => node is InvocationExpressionSyntax,
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
            Mode: PledgeModes.Parse(provider.GlobalOptions.TryGetValue(PledgeModes.ModeProperty, out var mode) ? mode : null),
            Metadata: PledgeModes.ParseMetadata(provider.GlobalOptions.TryGetValue(PledgeModes.MetadataProperty, out var metadata) ? metadata : null)));

        var interceptorsEnabled = context.ParseOptionsProvider.Select(static (parseOptions, _) => InterceptorsEnabled(parseOptions));

        context.RegisterSourceOutput(methods, static (productionContext, method) => ReportProblems(productionContext, method));
        context.RegisterSourceOutput(misplaced, static (productionContext, contract) =>
            productionContext.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedTarget, contract.Site.ToLocation(), contract.Member, contract.Problem)));

        var collected = methods.Collect();
        context.RegisterSourceOutput(collected.Combine(options), static (productionContext, input) => EmitMetadata(productionContext, input.Left, input.Right.Metadata));

        var input = collected.Combine(callSites.Collect()).Combine(options.Combine(interceptorsEnabled));
        context.RegisterSourceOutput(input, static (productionContext, input) =>
            EmitWrappers(productionContext, input.Left.Left, input.Left.Right, input.Right.Left.Mode, input.Right.Right));
    }

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

    private static void EmitWrappers(
        SourceProductionContext context,
        ImmutableArray<ContractedMethod> methods,
        ImmutableArray<CallSite> callSites,
        PledgeMode mode,
        bool interceptorsEnabled)
    {
        if (mode == PledgeMode.Ignore || methods.IsEmpty)
        {
            return;
        }

        if (!interceptorsEnabled)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.GeneratorNotActive, methods[0].Site.ToLocation(), WrapperEmitter.Namespace));
            return;
        }

        var sitesByMethod = callSites
            .GroupBy(site => site.MethodKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CallSite>)group.OrderBy(site => site.FilePath, StringComparer.Ordinal).ThenBy(site => site.Line).ToList(), StringComparer.Ordinal);

        foreach (var method in methods.OrderBy(method => method.HintName, StringComparer.Ordinal))
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!method.IsGenerable)
            {
                continue;
            }

            if (!sitesByMethod.TryGetValue(method.Key, out var sites))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoCallSites, method.Site.ToLocation(), method.MethodName));
                continue;
            }

            context.AddSource(method.HintName, WrapperEmitter.Emit(method, sites, mode));
        }
    }
}
