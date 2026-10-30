using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using PledgeContracts.Roslyn.Generator;

namespace PledgeContracts.Roslyn.Analyzers;

// Checks each Pledge call where it is written: that it sits at the top of a method body (PLEDGE001), that Result
// and Old appear where they have a meaning (PLEDGE002, PLEDGE003), that the expression is pure (PLEDGE004) and that
// Old does not snapshot a reference whose contents the body may change (PLEDGE007). Contracts in places the
// generator cannot intercept at all are the generator's PLEDGE009, not this analyzer's business.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractAnalyzer : DiagnosticAnalyzer
{
    public const string ResultTypeProperty = "ResultType";
    public const string CopyProperty = "Copy";
    public const string CopyWithToArray = "ToArray";
    public const string CopyWithWith = "With";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Diagnostics.ContractNotAtTop,
        Diagnostics.ResultOutsideEnsures,
        Diagnostics.ResultOnThrow,
        Diagnostics.ResultInOld,
        Diagnostics.ResultInVoidMethod,
        Diagnostics.ResultTypeMismatch,
        Diagnostics.OldOutsidePostcondition,
        Diagnostics.ImpureContract,
        Diagnostics.OldOfMutableReference);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(ContractParser.PledgeTypeName) is not { } pledge)
            {
                return;
            }

            var purity = new Purity(start.Compilation);
            start.RegisterSyntaxNodeAction(context => Analyze(context, pledge, purity), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol pledge, Purity purity)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (ContractSyntax.NameOf(invocation.Expression) is not ("Requires" or "Ensures" or "EnsuresOnThrow" or "Result" or "Old")
            || PledgeMember(invocation, context.SemanticModel, pledge, context.CancellationToken) is not { } method)
        {
            return;
        }

        switch (method.Name)
        {
            case "Result":
                if (EnclosingContract(invocation, context.SemanticModel, pledge, context.CancellationToken) is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ResultOutsideEnsures, invocation.GetLocation()));
                }

                return;

            case "Old":
                if (EnclosingContract(invocation, context.SemanticModel, pledge, context.CancellationToken) is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.OldOutsidePostcondition, invocation.GetLocation()));
                }

                return;

            default:
                AnalyzeContract(context, invocation, method, pledge, purity);
                return;
        }
    }

    private static void AnalyzeContract(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, IMethodSymbol method, INamedTypeSymbol pledge, Purity purity)
    {
        CheckPlacement(context, invocation, method, pledge);

        if (ContractParser.Argument(invocation, method, "condition") is not { } condition)
        {
            return;
        }

        var kind = method.Name switch
        {
            "Requires" => ContractKind.Precondition,
            "Ensures" => ContractKind.Postcondition,
            _ => ContractKind.PostconditionOnThrow,
        };

        var enclosingMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } declaration
            ? context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken)
            : null;

        new ExpressionWalker(context, pledge, purity, kind, enclosingMethod, condition.Span).Visit(condition);
    }

    // The generator reads contracts from the top of a block body and stops at the first other statement, so a
    // contract anywhere else in a method body is dead code. Other containers (accessors, lambdas, constructors) are
    // the generator's PLEDGE009.
    private static void CheckPlacement(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, IMethodSymbol method, INamedTypeSymbol pledge)
    {
        if (invocation.Parent is ExpressionStatementSyntax { Parent: BlockSyntax { Parent: MethodDeclarationSyntax } body } statement)
        {
            foreach (var previous in body.Statements)
            {
                if (previous == statement)
                {
                    return;
                }

                if (!ContractSyntax.IsContractCall(previous, out var earlier) || PledgeMember(earlier, context.SemanticModel, pledge, context.CancellationToken) is null)
                {
                    break;
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ContractNotAtTop, invocation.GetLocation(), method.Name));
            return;
        }

        var container = invocation.Ancestors().FirstOrDefault(static node =>
            node is MemberDeclarationSyntax or AccessorDeclarationSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
        if (container is MethodDeclarationSyntax { Body: not null })
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ContractNotAtTop, invocation.GetLocation(), method.Name));
        }
    }

    private static IMethodSymbol? PledgeMember(InvocationExpressionSyntax invocation, SemanticModel model, INamedTypeSymbol pledge, CancellationToken cancellationToken) =>
        model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method && SymbolEqualityComparer.Default.Equals(method.ContainingType, pledge)
            ? method
            : null;

    private static InvocationExpressionSyntax? EnclosingContract(InvocationExpressionSyntax invocation, SemanticModel model, INamedTypeSymbol pledge, CancellationToken cancellationToken)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            if (ancestor is MemberDeclarationSyntax)
            {
                return null;
            }

            if (ancestor is InvocationExpressionSyntax candidate
                && ContractSyntax.NameOf(candidate.Expression) is "Requires" or "Ensures" or "EnsuresOnThrow"
                && PledgeMember(candidate, model, pledge, cancellationToken) is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class ExpressionWalker(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol pledge,
        Purity purity,
        ContractKind kind,
        IMethodSymbol? enclosingMethod,
        Microsoft.CodeAnalysis.Text.TextSpan conditionSpan) : CSharpSyntaxWalker
    {
        private int _insideOld;

        private SemanticModel Model => context.SemanticModel;

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = Model.GetSymbolInfo(node, context.CancellationToken).Symbol as IMethodSymbol;
            if (symbol is null)
            {
                // nameof and unbound calls: nothing to say about the callee, the operands may still matter.
                if (ContractSyntax.NameOf(node.Expression) != "nameof")
                {
                    base.VisitInvocationExpression(node);
                }

                return;
            }

            if (SymbolEqualityComparer.Default.Equals(symbol.ContainingType, pledge))
            {
                switch (symbol.Name)
                {
                    case "Result":
                        CheckResult(node, symbol);
                        return;
                    case "Old":
                        CheckOld(node);
                        _insideOld++;
                        base.VisitInvocationExpression(node);
                        _insideOld--;
                        return;
                    default:
                        base.VisitInvocationExpression(node);
                        return;
                }
            }

            if (purity.Impurity(symbol) is { } impurity)
            {
                Report(Diagnostics.ImpureContract, node, impurity);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (!IsExpressionLocal(node.Left))
            {
                Report(Diagnostics.ImpureContract, node, $"assigns to '{node.Left}'");
            }

            base.VisitAssignmentExpression(node);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            if (node.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression && !IsExpressionLocal(node.Operand))
            {
                Report(Diagnostics.ImpureContract, node, $"modifies '{node.Operand}'");
            }

            base.VisitPrefixUnaryExpression(node);
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            if (node.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression && !IsExpressionLocal(node.Operand))
            {
                Report(Diagnostics.ImpureContract, node, $"modifies '{node.Operand}'");
            }

            base.VisitPostfixUnaryExpression(node);
        }

        public override void VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            Report(Diagnostics.ImpureContract, node, "awaits, and a contract must be evaluated synchronously");
            base.VisitAwaitExpression(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            if (node.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword
                && node.Expression is not DeclarationExpressionSyntax
                && !IsExpressionLocal(node.Expression))
            {
                Report(Diagnostics.ImpureContract, node, $"passes '{node.Expression}' by reference, so the callee may change it");
            }

            base.VisitArgument(node);
        }

        private void CheckResult(InvocationExpressionSyntax node, IMethodSymbol result)
        {
            if (_insideOld > 0)
            {
                Report(Diagnostics.ResultInOld, node);
                return;
            }

            switch (kind)
            {
                case ContractKind.PostconditionOnThrow:
                    Report(Diagnostics.ResultOnThrow, node);
                    return;
                case ContractKind.Precondition:
                    Report(Diagnostics.ResultOutsideEnsures, node);
                    return;
            }

            if (enclosingMethod is null)
            {
                return;
            }

            var (_, resultType) = ContractParser.Classify(enclosingMethod, Model.Compilation);
            if (resultType is null)
            {
                Report(Diagnostics.ResultInVoidMethod, node, enclosingMethod.Name);
            }
            else if (result.TypeArguments.Length == 1 && !SymbolEqualityComparer.Default.Equals(result.TypeArguments[0], resultType))
            {
                var properties = ImmutableDictionary<string, string?>.Empty.Add(ResultTypeProperty, resultType.ToDisplayString(DisplayFormats.Type));
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ResultTypeMismatch,
                    node.GetLocation(),
                    properties,
                    enclosingMethod.Name,
                    resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }

        private void CheckOld(InvocationExpressionSyntax node)
        {
            if (kind == ContractKind.Precondition)
            {
                Report(Diagnostics.OldOutsidePostcondition, node);
                return;
            }

            if (node.ArgumentList.Arguments.Count != 1)
            {
                return;
            }

            var argument = node.ArgumentList.Arguments[0].Expression;
            var type = Model.GetTypeInfo(argument, context.CancellationToken).Type;
            if (type is null || !MutableReferences.IsMutableReference(type) || MutableReferences.IsCopy(argument) || MutableReferences.IsComparedByIdentity(node, type, Model, context.CancellationToken))
            {
                return;
            }

            var properties = ImmutableDictionary<string, string?>.Empty.Add(CopyProperty, MutableReferences.SuggestedCopy(type));
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.OldOfMutableReference,
                node.GetLocation(),
                properties,
                argument.ToString(),
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        // Writes to locals and parameters that exist only inside the expression (lambda locals, out var) do not leak.
        private bool IsExpressionLocal(ExpressionSyntax target)
        {
            var symbol = Model.GetSymbolInfo(target, context.CancellationToken).Symbol;
            if (symbol is not (ILocalSymbol or IParameterSymbol))
            {
                return false;
            }

            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                if (reference.SyntaxTree == target.SyntaxTree && conditionSpan.Contains(reference.Span))
                {
                    return true;
                }
            }

            return false;
        }

        private void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object[] messageArgs) =>
            context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), messageArgs));
    }
}
