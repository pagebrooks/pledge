# PLEDGE007: `Old(...)` of a mutable reference

**Severity:** Warning. **Fix:** Snapshot a copy with `.ToArray()` (sequences) or `with { }` (records).

`Pledge.Old(x)` stores the value of `x` at entry. For a reference type that value is the reference, so
if the body mutates the object, the snapshot sees the mutation and a postcondition comparing the two
compares an object with itself.

```csharp
public void Add(Item item)
{
    Pledge.Ensures(Items.Count == Pledge.Old(Items).Count + 1);   // PLEDGE007: same list, always false
    Items.Add(item);
}
```

Copy what you want to compare: `Pledge.Old(Items.Count)`, `Pledge.Old(Items.ToArray())`,
`Pledge.Old(cursor with { })`, `Pledge.Old(new Snapshot(state))`.

The analyzer is quiet when the type cannot change (value types, `string`, delegates, immutable and
frozen collections, classes and records whose instance members are all read-only or init-only), when
the argument already creates a copy (a `with` expression, a constructor, a collection expression, a
`To*`/`Clone`/`Copy` call), and when the snapshot is only compared by identity (`Old(x) == x` on a type
without a custom `==`, `ReferenceEquals(Old(x), x)`, `Old(x) is null`). Interface types such as
`IReadOnlyList<T>` and `IEnumerable<T>` are reported: a read-only view still observes the live object.
