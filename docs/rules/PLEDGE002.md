# PLEDGE002: `Result<T>()` used incorrectly

**Severity:** Error. **Fix:** Use `Result<T>()` with the method's result type (for the type mismatch).

`Pledge.Result<T>()` stands for the value the method returns, so it only has a meaning inside
`Pledge.Ensures` of a method that returns something. The analyzer reports it when it appears:

- outside any contract, or inside `Pledge.Requires` (there is no result before the body runs);
- inside `Pledge.EnsuresOnThrow` (the method threw, there is no result);
- inside `Pledge.Old(...)` (the snapshot is taken before the body runs);
- in a method that returns `void`, `Task` or `ValueTask`;
- with a type argument other than the method's result type. For `Task<T>` and `ValueTask<T>` the result
  type is `T`, not the task.

```csharp
public async Task<Order> PlaceAsync(Cart cart)
{
    Pledge.Ensures(Pledge.Result<Order>().Lines.Count == cart.Lines.Count);   // T is Order, not Task<Order>
    ...
}
```

The generator emits nothing for a method with this error, so there is no accompanying PLEDGE009.
