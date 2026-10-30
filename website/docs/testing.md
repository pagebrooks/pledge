---
title: Contracts as property tests
description: Pledge.Testing runs every contracted method as a property-based test with CsCheck.
---

# Contracts as property tests

`Pledge.Testing` turns the contracts you already wrote into property-based tests. For each contracted
method, [CsCheck](https://github.com/AnthonyLloyd/CsCheck) generates a receiver and arguments, the
preconditions throw away the inputs they reject, and the postconditions are the oracle. A failure is shrunk
to the smallest input that breaks the contract.

```bash
dotnet add package Pledge.Testing --prerelease
```

```csharp
using PledgeContracts.Testing;

[Fact]
public void Account_pledges_hold() => PledgeProperties.Check<Account>(iterations: 1_000);
```

A failure names the contract, the shrunk input and how to replay it:

```text
RoundingAccount.Withdraw(decimal) broke its pledge. Postcondition of RoundingAccount.Withdraw violated: Balance == Old(Balance) - amount
Counterexample:
  receiver = new RoundingAccount(542m)
  amount = 0.00000000000000000001701m
Replay this input with Seed = "0VtUVmxeZKh1" and Iterations = 1. CsCheck: 3 shrinks, 38 skipped, 100 total.
```

That bug, a withdrawal rounded to cents, passes every unit test written with whole cents. The property
test finds it on the first run.

## One test per method

The xUnit.net v3 and TUnit adapters make each contracted method its own test case:

```bash
dotnet add package Pledge.Testing.Xunit --prerelease   # or Pledge.Testing.TUnit
```

```csharp
using PledgeContracts.Testing.Xunit;

[Theory, PledgedMembers(typeof(Account), Iterations = 1_000)]
public Task Account_pledges_hold(PledgeCase pledge) => pledge.CheckAsync();
```

With TUnit, use `[Test, PledgedMembers(typeof(Account))]` from `PledgeContracts.Testing.TUnit`. A method
whose parameters cannot be generated, such as one taking a `Span<T>`, shows up as a skipped test with the
reason.

## Generated inputs

Receivers are built through their widest public constructor. Numbers, strings, booleans, enums, dates,
`Guid`, records, nullable values, arrays and the common collection interfaces are generated out of the box.
Supply a generator for anything else, or to aim the inputs:

```csharp
using CsCheck;

PledgeProperties.Check<Account>(new PledgePropertyOptions { Iterations = 1_000 }.Use(Gen.Decimal[0m, 10_000m]));
```

`Use<T>` replaces the generator for every value of type `T`, constructor arguments included.
`PledgePropertyOptions` also sets the seed, the number of threads, and whether an exception no
`EnsuresOnThrow` declares counts as a failure (it does by default).

## Release builds

The probes the tests run are emitted unless every kind of contract is `Ignore`, which is the Release default.
If your tests run against a Release build of the project under test, set
`<PledgeRegistry>true</PledgeRegistry>` in that project.
