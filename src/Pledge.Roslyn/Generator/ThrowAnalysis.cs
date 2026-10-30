using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Generator;

// Decides whether a contract expression can go without the try/catch that turns an exception from the expression
// into a violation with Cause. That try keeps the JIT from inlining __Checked, which is the whole 1.8 ns a sync check
// costs over a hand-written guard (benchmarks/, Oct 30, 2026), so it is left out when the expression cannot throw:
// constants, parameters, locals, Result() and Old() (locals in the wrapper), built-in operators that cannot overflow
// or divide, conversions and patterns that cannot fail, and fields or auto-properties read through a receiver that
// cannot be null. Whatever it cannot prove keeps the try, so leaving something out of this list costs speed, never
// behaviour. It reads syntax and the binding the rewriter has already done, with one GetTypeInfo per node and other
// queries only where built-in types do not settle the answer: GetOperation would build the method's whole operation
// tree, and every query has a fixed cost that adds up across the generator's incremental run.
internal sealed class ThrowAnalysis(SemanticModel model, INamedTypeSymbol pledgeType, ContractKind kind, CancellationToken cancellationToken)
{
    public static bool CannotThrow(ExpressionSyntax condition, SemanticModel model, INamedTypeSymbol pledgeType, ContractKind kind, CancellationToken cancellationToken) =>
        new ThrowAnalysis(model, pledgeType, kind, cancellationToken).Safe(condition);

    private bool Safe(ExpressionSyntax expression) => Safe(expression, out _);

    private bool Safe(ExpressionSyntax expression, out ITypeSymbol? type)
    {
        var info = model.GetTypeInfo(expression, cancellationToken);
        type = info.Type;
        if (type is { TypeKind: TypeKind.Dynamic })
        {
            return false;
        }

        // An implicit conversion between built-in value types is numeric, nullable or constant and cannot throw;
        // only one involving other types needs classifying.
        if (!SymbolEqualityComparer.Default.Equals(type, info.ConvertedType)
            && !(IsBuiltIn(type) && IsBuiltIn(info.ConvertedType))
            && !SafeImplicit(model.GetConversion(expression, cancellationToken)))
        {
            return false;
        }

        return SafeNode(expression, type);
    }

    private bool SafeNode(ExpressionSyntax expression, ITypeSymbol? type) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Safe(parenthesized.Expression),
        CheckedExpressionSyntax checkedExpression => Safe(checkedExpression.Expression),
        PostfixUnaryExpressionSyntax suppressed when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression) => Safe(suppressed.Operand),
        LiteralExpressionSyntax or DefaultExpressionSyntax or TypeOfExpressionSyntax or ThisExpressionSyntax => true,
        IdentifierNameSyntax name => SafeMember(name, receiver: null),
        MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) => SafeMember(access, access.Expression),
        InvocationExpressionSyntax invocation => model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, pledgeType)
            && method.Name is "Result" or "Old",
        BinaryExpressionSyntax binary => SafeBinary(binary, type),
        PrefixUnaryExpressionSyntax unary => SafeUnary(unary, type) && Safe(unary.Operand),
        CastExpressionSyntax cast => SafeCast(cast) && Safe(cast.Expression),
        IsPatternExpressionSyntax isPattern => SafePattern(isPattern.Pattern) && Safe(isPattern.Expression),
        _ => false,
    };

    private bool SafeMember(ExpressionSyntax node, ExpressionSyntax? receiver) => model.GetSymbolInfo(node, cancellationToken).Symbol switch
    {
        IParameterSymbol or ILocalSymbol => receiver is null,
        IFieldSymbol field => field.IsConst || (!field.IsStatic && SafeReceiver(receiver)),
        IPropertySymbol property => SafeProperty(property, receiver),
        _ => false,
    };

    // Only a getter written `get;` cannot throw: a getter with a body (field keyword included), a virtual or partial
    // property, a static one (its type initializer can throw) and an indexer can. Nullable<T>.HasValue never does.
    private bool SafeProperty(IPropertySymbol property, ExpressionSyntax? receiver)
    {
        if (property.Parameters.Length > 0)
        {
            return false;
        }

        if (property.ContainingType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return property.Name == "HasValue" && receiver is not null && Safe(receiver);
        }

        if (property.IsStatic || property.IsVirtual || property.IsAbstract || property.IsOverride
            || property.GetMethod is not { DeclaringSyntaxReferences: { Length: > 0 } getters }
            || !SafeReceiver(receiver))
        {
            return false;
        }

        foreach (var getter in getters)
        {
            if (getter.GetSyntax(cancellationToken) is not AccessorDeclarationSyntax { Body: null, ExpressionBody: null, Parent.Parent: PropertyDeclarationSyntax declaration }
                || declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return false;
            }
        }

        return true;
    }

    // A struct value cannot be null. this can be, before the call: the interceptor is handed whatever receiver the
    // caller had, so only an Ensures, which runs after the original call returned, knows this is not null.
    private bool SafeReceiver(ExpressionSyntax? receiver) => receiver switch
    {
        null or ThisExpressionSyntax => kind == ContractKind.Postcondition,
        _ => model.GetTypeInfo(receiver, cancellationToken).Type is { IsValueType: true } && Safe(receiver),
    };

    private bool SafeBinary(BinaryExpressionSyntax binary, ITypeSymbol? type)
    {
        if (binary.Kind() is SyntaxKind.IsExpression or SyntaxKind.AsExpression)
        {
            return Safe(binary.Left);
        }

        return Safe(binary.Left, out var left) && Safe(binary.Right, out var right) && SafeOperator(binary, type, left, right);
    }

    private bool SafeOperator(BinaryExpressionSyntax binary, ITypeSymbol? type, ITypeSymbol? left, ITypeSymbol? right)
    {
        var operatorKind = binary.Kind();

        // Built-in operands cannot have a user-defined operator between them, so only other types need binding.
        if (!(IsBuiltIn(left) && IsBuiltIn(right))
            && model.GetSymbolInfo(binary, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method)
        {
            // decimal and string may bind these to declared operators, which cannot throw.
            return method.ContainingType.SpecialType switch
            {
                SpecialType.System_Decimal => IsComparison(operatorKind),
                SpecialType.System_String => operatorKind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression,
                _ => false,
            };
        }

        return operatorKind switch
        {
            SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression =>
                !InCheckedContext(binary) && IsPrimitive(type),
            SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
                or SyntaxKind.BitwiseAndExpression or SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression
                or SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or SyntaxKind.UnsignedRightShiftExpression => true,
            _ => IsComparison(operatorKind),
        };
    }

    private bool SafeUnary(PrefixUnaryExpressionSyntax unary, ITypeSymbol? type)
    {
        if (model.GetSymbolInfo(unary, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
        {
            return false;
        }

        return unary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression or SyntaxKind.BitwiseNotExpression or SyntaxKind.UnaryPlusExpression => true,
            SyntaxKind.UnaryMinusExpression => !InCheckedContext(unary) && IsPrimitive(type),
            _ => false,
        };
    }

    // Casts that can fail (explicit reference, unboxing, Nullable<T> to T, anything to or from decimal) are out.
    private bool SafeCast(CastExpressionSyntax cast)
    {
        var source = model.GetTypeInfo(cast.Expression, cancellationToken).Type;
        var target = model.GetTypeInfo(cast.Type, cancellationToken).Type;
        if (target is null)
        {
            return false;
        }

        var conversion = model.ClassifyConversion(cast.Expression, target, isExplicitInSource: true);
        return SafeImplicit(conversion)
            || (conversion.IsExplicit && !conversion.IsUserDefined && (conversion.IsNumeric || conversion.IsEnumeration)
                && !InCheckedContext(cast) && IsPrimitive(source) && IsPrimitive(target));
    }

    private static bool SafeImplicit(Conversion conversion) =>
        conversion.IsIdentity
        || (conversion.IsImplicit && !conversion.IsUserDefined && (conversion.IsNumeric || conversion.IsNullable || conversion.IsBoxing
            || conversion.IsReference || conversion.IsConstantExpression || conversion.IsEnumeration || conversion.IsNullLiteral || conversion.IsDefaultLiteral));

    // Property and list patterns run members of the value, so they are out; the rest only test and compare.
    private static bool SafePattern(PatternSyntax pattern) => pattern switch
    {
        ConstantPatternSyntax or RelationalPatternSyntax or TypePatternSyntax or DeclarationPatternSyntax or DiscardPatternSyntax or VarPatternSyntax => true,
        ParenthesizedPatternSyntax parenthesized => SafePattern(parenthesized.Pattern),
        UnaryPatternSyntax negated => SafePattern(negated.Pattern),
        BinaryPatternSyntax binary => SafePattern(binary.Left) && SafePattern(binary.Right),
        _ => false,
    };

    // Contracts are top-level statements, so a checked(...) or unchecked(...) around the expression is the only
    // thing that overrides the project's CheckForOverflowUnderflow.
    private bool InCheckedContext(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null and not StatementSyntax; current = current.Parent)
        {
            if (current is CheckedExpressionSyntax checkedExpression)
            {
                return checkedExpression.IsKind(SyntaxKind.CheckedExpression);
            }
        }

        return model.Compilation.Options.CheckOverflow;
    }

    private static bool IsComparison(SyntaxKind operatorKind) => operatorKind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
        or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
        or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression;

    // The value types whose operators and implicit conversions are all built in: IsPrimitive's, bool and decimal.
    private static bool IsBuiltIn(ITypeSymbol? type) =>
        IsPrimitive(type) || Underlying(type)?.SpecialType is SpecialType.System_Boolean or SpecialType.System_Decimal;

    private static ITypeSymbol? Underlying(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    // The types whose built-in arithmetic cannot throw outside a checked context, lifted to Nullable<T> or not.
    private static bool IsPrimitive(ITypeSymbol? type)
    {
        type = Underlying(type);
        return type is not null && (type.TypeKind == TypeKind.Enum || type.SpecialType is SpecialType.System_Char
            or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_Single or SpecialType.System_Double);
    }
}
