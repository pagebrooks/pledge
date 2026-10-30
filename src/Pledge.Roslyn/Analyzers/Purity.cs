using Microsoft.CodeAnalysis;

namespace PledgeContracts.Roslyn.Analyzers;

// Decides whether a call inside a contract expression is known to be side-effect free. A method is pure when it or
// its type carries [Pure] (either attribute), when it is one of the BCL members everyone treats as queries, or when
// it is an operator. Property and field reads never reach here: they are assumed pure, as the BCL guidelines demand.
internal sealed class Purity(Compilation compilation)
{
    private static readonly HashSet<string> _pureNamespaces = new(StringComparer.Ordinal)
    {
        "System.Linq",
        "System.Numerics",
        "System.Collections.Immutable",
        "System.Collections.Frozen",
    };

    // Metadata names, so generic arity is explicit: every method on these types is pure.
    private static readonly HashSet<string> _pureTypes = new(StringComparer.Ordinal)
    {
        "System.Object", "System.Math", "System.MathF", "System.String", "System.Char", "System.Boolean",
        "System.Byte", "System.SByte", "System.Int16", "System.Int32", "System.Int64", "System.Int128",
        "System.UInt16", "System.UInt32", "System.UInt64", "System.UInt128", "System.IntPtr", "System.UIntPtr",
        "System.Single", "System.Double", "System.Half", "System.Decimal", "System.Guid", "System.DateTime",
        "System.DateTimeOffset", "System.TimeSpan", "System.DateOnly", "System.TimeOnly", "System.Enum",
        "System.Nullable", "System.Nullable`1", "System.MemoryExtensions", "System.StringComparer",
        "System.BitConverter", "System.Convert", "System.Version", "System.Uri", "System.Index", "System.Range",
        "System.HashCode", "System.Type", "System.ReadOnlySpan`1", "System.ReadOnlyMemory`1",
        "System.Text.Rune", "System.Text.RegularExpressions.Regex",
        "System.IComparable", "System.IComparable`1", "System.IEquatable`1", "System.IFormattable",
        "System.Collections.IComparer", "System.Collections.IEqualityComparer",
        "System.Collections.IStructuralEquatable", "System.Collections.IStructuralComparable",
        "System.Collections.Generic.Comparer`1", "System.Collections.Generic.EqualityComparer`1",
        "System.Collections.Generic.IComparer`1", "System.Collections.Generic.IEqualityComparer`1",
        "System.Collections.Generic.KeyValuePair", "System.Collections.Generic.KeyValuePair`2",
    };

    // Query-shaped members of the remaining System types (collections, arrays, spans): pure by name.
    private static readonly HashSet<string> _pureMethodNames = new(StringComparer.Ordinal)
    {
        "Equals", "GetHashCode", "ToString", "GetType", "CompareTo", "Compare",
        "Contains", "ContainsKey", "ContainsValue", "TryGetValue", "IndexOf", "LastIndexOf",
        "Exists", "TrueForAll", "Find", "FindIndex", "FindLast", "FindLastIndex", "FindAll", "BinarySearch",
        "GetValueOrDefault", "SequenceEqual", "StartsWith", "EndsWith", "ToArray", "ToList", "ToHashSet",
        "ToDictionary", "AsReadOnly", "AsSpan", "AsMemory", "GetEnumerator", "GetLength", "GetLowerBound",
        "GetUpperBound", "GetRange", "Slice", "Overlaps", "IsSubsetOf", "IsSupersetOf", "IsProperSubsetOf",
        "IsProperSupersetOf", "SetEquals", "Peek", "TryPeek",
    };

    // Members of object and the comparison interfaces are pure by contract wherever they are implemented.
    private static readonly HashSet<string> _pureEverywhere = new(StringComparer.Ordinal)
    {
        "Equals", "GetHashCode", "ToString", "GetType", "CompareTo",
    };

    private readonly INamedTypeSymbol? _bclPure = compilation.GetTypeByMetadataName("System.Diagnostics.Contracts.PureAttribute");
    private readonly INamedTypeSymbol? _pledgePure = compilation.GetTypeByMetadataName("PledgeContracts.PureAttribute");

    // Null when the call is known pure; otherwise the clause that completes "Contract expression is not pure: it ...".
    public string? Impurity(IMethodSymbol method)
    {
        if (method.MethodKind is MethodKind.BuiltinOperator or MethodKind.UserDefinedOperator or MethodKind.Conversion)
        {
            return null;
        }

        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            return "invokes a delegate, whose purity cannot be known";
        }

        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        if (IsMarkedPure(definition) || _pureEverywhere.Contains(definition.Name) || IsKnownPure(definition))
        {
            return null;
        }

        return $"calls '{definition.Name}', which is not marked [Pure]";
    }

    public bool IsMarkedPure(IMethodSymbol method)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            if (HasPureAttribute(current) || HasPureAttribute(current.ContainingType))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasPureAttribute(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _bclPure)
                || SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _pledgePure))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownPure(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type is null)
        {
            return false;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns != "System" && !ns.StartsWith("System.", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var pure in _pureNamespaces)
        {
            if (ns == pure || ns.StartsWith(pure + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        var typeName = ns + "." + type.MetadataName;
        if (_pureTypes.Contains(typeName) || typeName.StartsWith("System.ValueTuple", StringComparison.Ordinal) || typeName.StartsWith("System.Tuple", StringComparison.Ordinal))
        {
            return true;
        }

        return _pureMethodNames.Contains(method.Name);
    }
}
