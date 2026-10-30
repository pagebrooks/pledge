---
slug: /
title: Getting started
description: Design by Contract for .NET with preconditions, postconditions and Old() snapshots checked by a Roslyn source generator.
---

# Pledge

Pledge is Design by Contract for .NET 10 and later. You write preconditions, postconditions and `Old()`
snapshots as ordinary C# at the top of a method, and a Roslyn source generator checks them at build time
through C# interceptors. There is no IL rewriter, no compiler fork and no IDE extension.

:::note[Alpha]
Pledge is at `0.1.0-alpha`. The API can still change between pre-releases.
:::

## Install

```bash
dotnet add package Pledge --prerelease
```

That is the whole setup. The package carries the runtime, the generator, the analyzers with their code
fixes, and the build targets that turn on the interceptors feature for your project.

## Write a contract

```csharp
using PledgeContracts;

public sealed class Account(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= Balance, "insufficient funds");
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
        Pledge.Ensures(Pledge.Result<decimal>() >= 0);

        Balance -= amount;
        return Balance;
    }
}
```

The `Pledge.*` lines compile to nothing inside the method. Instead, every call to `Withdraw` in your
project is replaced at build time with a call that checks the contracts around it.

## Break it

```csharp
var account = new Account(100m);
account.Withdraw(250m);
```

In a Debug build this throws `PreconditionViolationException`, and the message names the contract, the
member and the caller:

```text
Precondition of Account.Withdraw violated by its caller: insufficient funds [amount <= Balance] (at /src/Program.cs:9)
```

A broken precondition is the caller's fault; a broken postcondition is the method's. In a Release build
the checks are off by default and the assembly is the one you would get without Pledge. See
[enforcement modes](modes.md) to change that.

## Next

- [When to use Pledge](use-cases.md): the situations contracts pay off in, and the ones where another tool fits better.
- [Validation or contracts?](validation-vs-contracts.md): how to tell the two apart and where each one goes.
- [Writing contracts](contracts.md): every member of `Pledge`, the placement rules and what each contract may say.
- [Enforcement modes](modes.md): `Ignore`, `Observe`, `Enforce` and `EnforceFailFast`, per build and per kind of contract.
- [Contracts as property tests](testing.md): `Pledge.Testing` runs the same contracts as property-based tests.
- [Diagnostics](/rules): what each `PLEDGE` analyzer reports and how to fix it.
- [Compared with other tools](comparison.md): Pledge next to Metalama, PostSharp, Code Contracts, Fody weavers and guard clauses, including what each one costs.
