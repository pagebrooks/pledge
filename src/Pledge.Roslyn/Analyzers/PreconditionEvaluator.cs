using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PledgeContracts.Roslyn.Analyzers;

// Evaluates a precondition with constants substituted for parameters, in three-valued logic: true, false or
// unknown. It understands literals, parameters, comparisons, && || ! ??, relational and constant patterns,
// string.Length and string.IsNullOrEmpty/IsNullOrWhiteSpace. Everything else is unknown, and unknown never
// becomes a diagnostic.
internal static class PreconditionEvaluator
{
    private static readonly object _unknown = new();

    // Null when the outcome depends on something that is not a constant; otherwise the condition's value. Every
    // parameter whose argument took part in the answer is added to used, in order of first use.
    public static bool? Evaluate(ExpressionSyntax condition, SemanticModel model, IReadOnlyDictionary<string, object?> arguments, List<string> used)
    {
        var result = Eval(condition, new Context(model, arguments, used));
        return result is bool value ? value : null;
    }

    private static object? Eval(ExpressionSyntax expression, Context context)
    {
        var constant = context.Model.GetConstantValue(expression);
        if (constant.HasValue)
        {
            return constant.Value;
        }

        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return Eval(parenthesized.Expression, context);

            case IdentifierNameSyntax identifier:
                if (context.Model.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter && context.Arguments.TryGetValue(parameter.Name, out var argument))
                {
                    if (!context.Used.Contains(parameter.Name))
                    {
                        context.Used.Add(parameter.Name);
                    }

                    return argument;
                }

                return _unknown;

            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not:
                return Eval(not.Operand, context) is bool operand ? !operand : _unknown;

            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } minus:
                return ToDecimal(Eval(minus.Operand, context)) is { } negated ? -negated : _unknown;

            case BinaryExpressionSyntax binary:
                return EvalBinary(binary, context);

            case IsPatternExpressionSyntax isPattern:
                var subject = Eval(isPattern.Expression, context);
                return ReferenceEquals(subject, _unknown) ? _unknown : Match(isPattern.Pattern, subject, context);

            case ConditionalExpressionSyntax conditional:
                return Eval(conditional.Condition, context) is bool test
                    ? Eval(test ? conditional.WhenTrue : conditional.WhenFalse, context)
                    : _unknown;

            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" } length:
                return Eval(length.Expression, context) is string text ? text.Length : _unknown;

            case InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } invocation
                when context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { IsStatic: true, ContainingType.SpecialType: SpecialType.System_String, Name: "IsNullOrEmpty" or "IsNullOrWhiteSpace" } method:
                var value = Eval(invocation.ArgumentList.Arguments[0].Expression, context);
                return value switch
                {
                    null => true,
                    string s => method.Name == "IsNullOrEmpty" ? s.Length == 0 : string.IsNullOrWhiteSpace(s),
                    _ => _unknown,
                };

            default:
                return _unknown;
        }
    }

    private static object? EvalBinary(BinaryExpressionSyntax binary, Context context)
    {
        var kind = binary.Kind();
        switch (kind)
        {
            case SyntaxKind.LogicalAndExpression:
            {
                var left = Eval(binary.Left, context);
                if (left is false)
                {
                    return false;
                }

                var right = Eval(binary.Right, context);
                return right is false ? false : left is true && right is true ? true : _unknown;
            }

            case SyntaxKind.LogicalOrExpression:
            {
                var left = Eval(binary.Left, context);
                if (left is true)
                {
                    return true;
                }

                var right = Eval(binary.Right, context);
                return right is true ? true : left is false && right is false ? false : _unknown;
            }

            case SyntaxKind.CoalesceExpression:
            {
                var left = Eval(binary.Left, context);
                return ReferenceEquals(left, _unknown) ? _unknown : left ?? Eval(binary.Right, context);
            }

            case SyntaxKind.EqualsExpression:
            case SyntaxKind.NotEqualsExpression:
            case SyntaxKind.LessThanExpression:
            case SyntaxKind.LessThanOrEqualExpression:
            case SyntaxKind.GreaterThanExpression:
            case SyntaxKind.GreaterThanOrEqualExpression:
            {
                var left = Eval(binary.Left, context);
                var right = Eval(binary.Right, context);
                return ReferenceEquals(left, _unknown) || ReferenceEquals(right, _unknown) ? _unknown : Compare(kind, left, right);
            }

            default:
                return _unknown;
        }
    }

    private static object? Match(PatternSyntax pattern, object? value, Context context)
    {
        switch (pattern)
        {
            case ParenthesizedPatternSyntax parenthesized:
                return Match(parenthesized.Pattern, value, context);

            case DiscardPatternSyntax:
                return true;

            case ConstantPatternSyntax constant:
                var expected = context.Model.GetConstantValue(constant.Expression);
                return expected.HasValue ? Compare(SyntaxKind.EqualsExpression, value, expected.Value) : _unknown;

            case RelationalPatternSyntax relational:
                var bound = context.Model.GetConstantValue(relational.Expression);
                if (!bound.HasValue)
                {
                    return _unknown;
                }

                var kind = relational.OperatorToken.Kind() switch
                {
                    SyntaxKind.LessThanToken => SyntaxKind.LessThanExpression,
                    SyntaxKind.LessThanEqualsToken => SyntaxKind.LessThanOrEqualExpression,
                    SyntaxKind.GreaterThanToken => SyntaxKind.GreaterThanExpression,
                    _ => SyntaxKind.GreaterThanOrEqualExpression,
                };
                return Compare(kind, value, bound.Value);

            case BinaryPatternSyntax { RawKind: (int)SyntaxKind.AndPattern } and:
            {
                var left = Match(and.Left, value, context);
                if (left is false)
                {
                    return false;
                }

                var right = Match(and.Right, value, context);
                return right is false ? false : left is true && right is true ? true : _unknown;
            }

            case BinaryPatternSyntax { RawKind: (int)SyntaxKind.OrPattern } or:
            {
                var left = Match(or.Left, value, context);
                if (left is true)
                {
                    return true;
                }

                var right = Match(or.Right, value, context);
                return right is true ? true : left is false && right is false ? false : _unknown;
            }

            case UnaryPatternSyntax not:
                return Match(not.Pattern, value, context) is bool matched ? !matched : _unknown;

            case TypePatternSyntax or DeclarationPatternSyntax or RecursivePatternSyntax:
                return value is null ? false : _unknown;

            default:
                return _unknown;
        }
    }

    private static object? Compare(SyntaxKind kind, object? left, object? right)
    {
        if (left is null || right is null)
        {
            return kind switch
            {
                SyntaxKind.EqualsExpression => left is null && right is null,
                SyntaxKind.NotEqualsExpression => !(left is null && right is null),
                _ => _unknown,
            };
        }

        if (left is bool leftBool && right is bool rightBool)
        {
            return kind switch
            {
                SyntaxKind.EqualsExpression => leftBool == rightBool,
                SyntaxKind.NotEqualsExpression => leftBool != rightBool,
                _ => _unknown,
            };
        }

        if (left is string leftText && right is string rightText)
        {
            return kind switch
            {
                SyntaxKind.EqualsExpression => string.Equals(leftText, rightText, StringComparison.Ordinal),
                SyntaxKind.NotEqualsExpression => !string.Equals(leftText, rightText, StringComparison.Ordinal),
                _ => _unknown,
            };
        }

        if (ToDecimal(left) is not { } a || ToDecimal(right) is not { } b)
        {
            return _unknown;
        }

        return kind switch
        {
            SyntaxKind.EqualsExpression => a == b,
            SyntaxKind.NotEqualsExpression => a != b,
            SyntaxKind.LessThanExpression => a < b,
            SyntaxKind.LessThanOrEqualExpression => a <= b,
            SyntaxKind.GreaterThanExpression => a > b,
            SyntaxKind.GreaterThanOrEqualExpression => a >= b,
            _ => _unknown,
        };
    }

    private static decimal? ToDecimal(object? value)
    {
        try
        {
            return value switch
            {
                byte or sbyte or short or ushort or int or uint or long or ulong or decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                char c => c,
                float f when !float.IsNaN(f) && !float.IsInfinity(f) => (decimal)f,
                double d when !double.IsNaN(d) && !double.IsInfinity(d) => (decimal)d,
                _ => null,
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private sealed record Context(SemanticModel Model, IReadOnlyDictionary<string, object?> Arguments, List<string> Used);
}
