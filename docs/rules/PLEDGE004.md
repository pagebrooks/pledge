# PLEDGE004: Impure expression in a contract

**Severity:** Warning (error in v1.0). **Fix:** Mark the callee `[Pure]`, when it is declared in the solution.

Contracts run in `Enforce` and `Observe` builds and are removed in `Ignore` builds. An expression with a
side effect therefore makes the program behave differently between Debug and Release, which is the
classic contracts heisenbug. The analyzer reports a contract expression that:

- assigns, increments or decrements anything that outlives the expression;
- `await`s;
- passes a variable by `ref` or `out` (an `out var` declaration inside the expression is fine);
- invokes a delegate, whose purity cannot be known;
- calls a method that is not known to be pure.

A method is known pure when it, its containing type, or a method it overrides carries `[Pure]`, either
`PledgeContracts.PureAttribute` or `System.Diagnostics.Contracts.PureAttribute`. Operators, the members
of `object` and the comparison interfaces (`Equals`, `GetHashCode`, `ToString`, `CompareTo`), and a
BCL allow-list are pure without an attribute: `System.Linq`, `System.Numerics`, the immutable and frozen
collections, `Math`, `string`, the primitive and date types, `Nullable<T>`, `Regex`, `Convert`, and the
query-shaped members of the collections (`Contains`, `ContainsKey`, `TryGetValue`, `IndexOf`,
`SequenceEqual`, `ToArray`, ...). Property, field and indexer reads are always taken as pure.

```csharp
public void Ship(Order order)
{
    Pledge.Requires(IsShippable(order));   // PLEDGE004 unless IsShippable is [Pure]
    ...
}

[Pure]
private static bool IsShippable(Order order) => order.Lines.Count > 0;
```

Marking a method `[Pure]` is a promise the analyzer takes at face value.
