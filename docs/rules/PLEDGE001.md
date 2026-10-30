# PLEDGE001: Contract call is not at the top of the method

**Severity:** Error. **Fix:** Move contract to the top of the method.

The generator reads `Pledge.Requires`, `Pledge.Ensures` and `Pledge.EnsuresOnThrow` from the top of a
method body and stops at the first statement that is not one of them. A contract after that point, or
nested inside another statement, is never lifted into a check. Because `Pledge.*` calls compile away in
every build, it would also never run as plain code: it would simply vanish.

```csharp
public decimal Withdraw(decimal amount)
{
    var fee = Fee(amount);
    Pledge.Requires(amount > 0);   // PLEDGE001: after a non-contract statement
    ...
}
```

Move the contract above the first ordinary statement. The code fix does this when the contract does not
read a local declared above it; a contract that needs such a local has to be rewritten in terms of
parameters and members, since it is evaluated before the body runs.

Contracts in places the generator cannot intercept at all (constructors, property accessors, lambdas)
are reported as [PLEDGE009](PLEDGE009.md) instead.
