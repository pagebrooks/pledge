# PLEDGE003: `Old(...)` used outside a postcondition

**Severity:** Error.

`Pledge.Old(expression)` is the value of `expression` when the method was entered, for comparison with
its value when the method exits. That comparison only exists in `Pledge.Ensures` and
`Pledge.EnsuresOnThrow`. In a `Pledge.Requires`, or outside any contract, there is no "after" to compare
with, and the call would compile to nothing.

```csharp
public void Deposit(decimal amount)
{
    Pledge.Requires(Pledge.Old(Balance) >= 0);   // PLEDGE003: a precondition sees the entry value already
    Pledge.Ensures(Balance == Pledge.Old(Balance) + amount);   // fine
    ...
}
```
