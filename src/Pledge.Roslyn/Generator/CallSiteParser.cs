#pragma warning disable RSEXPERIMENTAL002 // GetInterceptableLocation: the one interceptor API Roslyn offers; wrapped here so a change is a one-file fix.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Finds invocations of contracted methods declared in the same compilation and records where to intercept them.
internal static class CallSiteParser
{
    public static CallSite? Parse(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
        {
            return null;
        }

        var implementation = method.OriginalDefinition.PartialImplementationPart ?? method.OriginalDefinition;
        if (implementation.DeclaringSyntaxReferences.Length == 0
            || implementation.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration
            || !ContractSyntax.IsCandidateDeclaration(declaration))
        {
            return null;
        }

        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var lineSpan = invocation.GetLocation().GetLineSpan();
        return new CallSite(
            implementation.ToDisplayString(DisplayFormats.MethodKey),
            location.Version,
            location.Data,
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1);
    }
}
