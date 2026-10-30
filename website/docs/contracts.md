---
title: Writing contracts
description: The members of Pledge, where contracts go, what they may say, and where they are checked.
---

# Writing contracts

Contracts are calls to the static class `Pledge` in the `PledgeContracts` namespace, written as the first
statements of a method body.

| Member | Meaning |
| --- | --- |
| `Pledge.Requires(condition, message?)` | Precondition. Checked before the body runs; a failure blames the caller. |
| `Pledge.Ensures(condition, message?)` | Postcondition. Checked when the method returns normally; a failure blames the method. |
| `Pledge.Result<T>()` | The method's return value, inside an `Ensures`. `T` must be the return type (the awaited type for async methods). |
| `Pledge.Old(expression)` | The value of the expression when the method was entered, inside a postcondition. |
| `Pledge.EnsuresOnThrow<TException>(condition, message?)` | Postcondition checked when the method throws `TException` or a subtype. The exception is always rethrown. |

```csharp
public decimal Withdraw(decimal amount)
{
    Pledge.Requires(amount > 0);
    Pledge.Requires(amount <= Balance, "insufficient funds");
    Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
    Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));

    Balance -= amount;
    return Balance;
}
```

The message is optional; without one, the violation reports the contract's own text.

## Rules

- **Contracts come first.** They must be the first statements of the body. A contract after other code is
  an error ([PLEDGE001](/rules/PLEDGE001)), not silently ignored.
- **Contracts are pure.** No assignments, no `await`, no calls that could change state. Property and field
  reads, operators, LINQ, `Math`, `string` and the query members of the collections are fine. Anything else
  needs `[Pure]` ([PLEDGE004](/rules/PLEDGE004)), and the code fix adds it.
- **`Old` copies values, not objects.** `Pledge.Old(items)` keeps the reference, so a change the body makes
  to the list shows in the snapshot too. Snapshot a copy, such as `Pledge.Old(items.ToArray())`
  ([PLEDGE007](/rules/PLEDGE007)).
- **What a contract can see.** A contract runs outside the method, at the call site, so it may only use what a
  caller could: parameters, and public or internal members.

## Order of evaluation

Preconditions run in source order, then the `Old` snapshots are taken, then the body runs, then the
postconditions are checked in source order. A failing precondition stops there: the body never runs.

A contract expression that throws is reported as a violation of that contract, with the exception as its
`Cause`.

## Async methods

Methods returning `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>` are supported. Preconditions run
before the task is created, so a broken precondition throws at the call rather than through a faulted task.
Postconditions run after the task completes, and `Result<T>()` is the awaited value. Cancellation skips
`EnsuresOnThrow` unless a clause names a cancellation exception type.

## Where contracts are checked

Pledge checks a contract where the method is called, not inside it: the compiler swaps each call for a
checked one. So contracts run on calls made inside the project that declares them. They do not run on:

- calls from other assemblies;
- calls through a delegate, such as a method group passed to `Select`;
- calls through an interface or a base class, which bind to that member rather than to the contracted one;
- reflection and `dynamic`.

The analyzers point out each of these in your project as [PLEDGE006](/rules/PLEDGE006). Where it matters,
call the contracted method directly, or wrap it in a lambda (`x => account.Withdraw(x)` is an ordinary call
and is checked).

## Supported members

Public and internal instance and static methods of classes, sync or async. Contracts on anything else
compile to no-ops and report [PLEDGE009](/rules/PLEDGE009): properties, constructors, operators, lambdas
and local functions, generic methods and methods of generic types, struct members, extension methods,
expression-bodied methods, and private or protected members. Properties, invariants, contract
inheritance and interface contracts are planned for v1.0.
