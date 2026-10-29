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

        var mode = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => PledgeModes.Parse(provider.GlobalOptions.TryGetValue(PledgeModes.Property, out var value) ? value : null));

        var interceptorsEnabled = context.ParseOptionsProvider
            .Select(static (options, _) => InterceptorsEnabled(options));

        context.RegisterSourceOutput(methods, static (productionContext, method) => ReportProblems(productionContext, method));

        var input = methods.Collect().Combine(callSites.Collect()).Combine(mode.Combine(interceptorsEnabled));
        context.RegisterSourceOutput(input, static (productionContext, input) =>
            Emit(productionContext, input.Left.Left, input.Left.Right, input.Right.Left, input.Right.Right));
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

    private static void Emit(
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

            if (method.Problems.Count > 0 || !sitesByMethod.TryGetValue(method.Key, out var sites))
            {
                continue;
            }

            context.AddSource(method.HintName, WrapperEmitter.Emit(method, sites, mode));
        }
    }
}
