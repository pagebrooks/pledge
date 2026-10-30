using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PledgeContracts.Roslyn.Generator;

namespace PledgeContracts.Roslyn.Analyzers;

// Old(x) stores the reference x holds, so if the body mutates the object the snapshot sees the change. These
// heuristics decide when that is worth a warning: the type can change, the expression is not already a copy, and
// the snapshot is not merely compared by identity.
internal static class MutableReferences
{
    private static readonly HashSet<string> _immutableTypes = new(StringComparer.Ordinal)
    {
        "System.Object", "System.String", "System.Type", "System.Uri", "System.Version", "System.Delegate",
        "System.MulticastDelegate", "System.Exception", "System.Text.RegularExpressions.Regex",
    };

    public static bool IsMutableReference(ITypeSymbol type)
    {
        if (!type.IsReferenceType || type.TypeKind is TypeKind.Delegate or TypeKind.Error or TypeKind.TypeParameter)
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns.StartsWith("System.Collections.Immutable", StringComparison.Ordinal) || ns.StartsWith("System.Collections.Frozen", StringComparison.Ordinal))
        {
            return false;
        }

        if (type is not INamedTypeSymbol named || _immutableTypes.Contains(ns + "." + named.MetadataName))
        {
            return false;
        }

        if (named.TypeKind == TypeKind.Interface)
        {
            // Read-only views (IReadOnlyList<T>, IEnumerable<T>) still observe the live object.
            return true;
        }

        for (var current = named; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                switch (member)
                {
                    case IFieldSymbol { IsStatic: false, IsReadOnly: false, IsConst: false }:
                        return true;
                    case IPropertySymbol { IsStatic: false, SetMethod: { IsInitOnly: false } }:
                        return true;
                }
            }
        }

        return false;
    }

    // Expressions that already produce a fresh object: with expressions, constructors, collection expressions and
    // the conventional copying calls (ToArray, ToList, ToImmutableArray, Clone, ...).
    public static bool IsCopy(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression switch
        {
            WithExpressionSyntax => true,
            BaseObjectCreationExpressionSyntax => true,
            ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax => true,
            InterpolatedStringExpressionSyntax => true,
            InvocationExpressionSyntax invocation => ContractSyntax.NameOf(invocation.Expression) is { } name
                && (name.StartsWith("To", StringComparison.Ordinal) || name is "Clone" or "Copy" or "MemberwiseClone" or "Slice"),
            _ => false,
        };
    }

    // Old(x) == x, Old(x) != x, ReferenceEquals(Old(x), x) and Old(x) is null ask about the reference itself, which a
    // reference snapshot answers correctly, unless the type overloads == to compare contents.
    public static bool IsComparedByIdentity(InvocationExpressionSyntax old, ITypeSymbol type, SemanticModel model, CancellationToken cancellationToken)
    {
        SyntaxNode node = old;
        while (node.Parent is ParenthesizedExpressionSyntax parent)
        {
            node = parent;
        }

        switch (node.Parent)
        {
            case BinaryExpressionSyntax binary when binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression:
                return !OverloadsEquality(type);
            case IsPatternExpressionSyntax { Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } } }:
            case IsPatternExpressionSyntax { Pattern: UnaryPatternSyntax { Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } } } }:
                return true;
            case ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax call }:
                return model.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol { Name: "ReferenceEquals", ContainingType.SpecialType: SpecialType.System_Object };
            default:
                return false;
        }
    }

    public static string SuggestedCopy(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol || type.AllInterfaces.Any(static i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            || type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return ContractAnalyzer.CopyWithToArray;
        }

        return type is INamedTypeSymbol { IsRecord: true } ? ContractAnalyzer.CopyWithWith : string.Empty;
    }

    private static bool OverloadsEquality(ITypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current is INamedTypeSymbol { IsRecord: true } || current.GetMembers("op_Equality").Length > 0)
            {
                return true;
            }
        }

        return false;
    }
}
