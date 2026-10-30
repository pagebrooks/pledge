---
title: When to use Pledge
description: Scenarios where contracts pay off, from domain models and refactoring to AI-written code, and the ones where another tool fits better.
---

# When to use Pledge

Contracts pay off wherever code has rules that are easy to state and easy to break: what a method needs, what
it changes, and what must still be true afterwards. These are the situations Pledge is built for.

## Methods that change state

Most bugs in domain code are not crashes. The method runs, returns, and leaves the object a little wrong.
A postcondition says exactly what the method was supposed to change, and `Old()` lets it compare with how
things were before.

```csharp
public sealed class Inventory
{
    private readonly Dictionary<string, int> _stock = [];
    private readonly Dictionary<string, int> _reserved = [];

    [Pure]
    public int Available(string sku) => _stock.GetValueOrDefault(sku) - _reserved.GetValueOrDefault(sku);

    [Pure]
    public int Reserved(string sku) => _reserved.GetValueOrDefault(sku);

    public void Reserve(string sku, int quantity)
    {
        Pledge.Requires(quantity > 0);
        Pledge.Requires(Available(sku) >= quantity, "not enough stock");
        Pledge.Ensures(Available(sku) == Pledge.Old(Available(sku)) - quantity);
        Pledge.Ensures(Reserved(sku) == Pledge.Old(Reserved(sku)) + quantity);

        _reserved[sku] = Reserved(sku) + quantity;
    }
}
```

The contracts double as documentation that cannot drift: if the body stops doing what they say, the next
Debug run tells you.

## Leaving things as they were when a method fails

`EnsuresOnThrow` states what must hold when a method gives up partway through: usually, that nothing changed.
That is the hardest promise to keep by hand, and the one hand-written tests rarely cover.

```csharp
public static void Transfer(Account from, Account to, decimal amount)
{
    Pledge.Requires(amount > 0);
    Pledge.Ensures(from.Balance + to.Balance == Pledge.Old(from.Balance + to.Balance));
    Pledge.EnsuresOnThrow<InvalidOperationException>(
        from.Balance == Pledge.Old(from.Balance) && to.Balance == Pledge.Old(to.Balance));

    from.Withdraw(amount);
    to.Deposit(amount);
}
```

If `Deposit` throws an `InvalidOperationException` after `Withdraw` succeeded, the money has left one account
without reaching the other. The on-throw postcondition reports that, instead of letting it pass silently.

## Refactoring with a safety net

Before you rewrite a method, write down what it must keep doing. The contracts check the new body in every
Debug run, and [`Pledge.Testing`](testing.md) runs them against thousands of generated inputs, including the
edge cases nobody wrote a test for.

```csharp
public static decimal ApplyDiscount(decimal price, decimal percent)
{
    Pledge.Requires(price >= 0);
    Pledge.Requires(percent is >= 0 and <= 100);
    Pledge.Ensures(Pledge.Result<decimal>() >= 0 && Pledge.Result<decimal>() <= price);

    return Math.Round(price * (100 - percent) / 100, 2);
}
```

Rounding to cents can push a price that has a fraction of a cent above the original: `ApplyDiscount(0.006m, 0)`
returns `0.01`. The unit tests,
written with whole prices, never find that; a property test does, and shrinks it to the smallest input that
shows it.

## Specifying code someone else writes

A signature says what types go in and out. Contracts say what the method has to do. That makes them a
precise brief for a teammate, a new hire, or an AI coding assistant: you write the signature and the
`Requires` and `Ensures` lines, someone else writes the body, and the contracts check the result. The
generated property tests then try the body against inputs neither of you thought of.

## Adding contracts to code that is already running

You do not have to be sure every contract is right before you ship it. Build with `Observe`, route
violations to your logs, and run the code where it meets real traffic:

```xml
<PropertyGroup>
  <PledgeMode>Observe</PledgeMode>
</PropertyGroup>
```

```csharp
PledgeRuntime.ViolationHandler = violation =>
    logger.LogWarning("{Kind} of {Member} violated: {Expression}", violation.Kind, violation.Member, violation.Expression);
```

Each violation is either a bug in the code or a contract that was wrong. Fix whichever one it is, and switch
to `Enforce` once the logs are quiet.

## Hot paths and shipped binaries

Contracts do not have to cost anything you ship. In `Ignore`, the default for Release builds, the assembly is
byte-identical to one built without Pledge. If you do keep the checks on, simple contracts compile to the
same code as a hand-written guard. See [performance](performance.md) for the numbers.

## Native AOT and trimmed apps

The checks are ordinary generated C#, with no reflection and no runtime proxies, so they work with trimming
and Native AOT. CI publishes a sample with Native AOT on every push.

## When something else fits better

- **Input from users and other systems.** A bad form field or request body is not a bug, and in Release
  contracts are off by default. Use validation; see [Validation or contracts?](validation-vs-contracts.md).
- **Nullness.** Nullable reference types catch most null mistakes at compile time, for free.
- **The public API of a library.** Pledge checks calls in the project being compiled, so a call from a
  consumer's assembly is not checked. Use guard clauses on those entry points and contracts behind them.
- **Older runtimes.** Pledge needs .NET 10 and C# 14 or later.
- **Proof instead of checking.** If a bug must be impossible rather than caught, look at a verified language
  such as [Dafny](https://dafny.org).
