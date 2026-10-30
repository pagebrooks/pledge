# Pledge.Testing

Turns [Pledge](https://www.nuget.org/packages/Pledge) contracts into property-based tests. For
each contracted member, [CsCheck](https://github.com/AnthonyLloyd/CsCheck) generates a receiver
and arguments, the preconditions throw away the inputs they reject, and the postconditions are the
oracle. A failure is shrunk to the smallest input that breaks the contract. Nothing is written twice.

> **Alpha.** The API can still change between pre-releases.

```sh
dotnet add package Pledge.Testing --prerelease
```

```csharp
using PledgeContracts.Testing;

[Fact]
public void Account_pledges_hold() => PledgeProperties.Check<Account>(iterations: 1_000);
```

```text
RoundingAccount.Withdraw(decimal) broke its pledge. Postcondition of RoundingAccount.Withdraw violated: Balance == Old(Balance) - amount
Counterexample:
  receiver = new RoundingAccount(542m)
  amount = 0.00000000000000000001701m
Replay this input with Seed = "0VtUVmxeZKh1" and Iterations = 1. CsCheck: 3 shrinks, 38 skipped, 100 total.
```

Receivers are built through their widest public constructor. Primitives, strings, enums, dates,
records, nullable values and common collections are generated out of the box. Supply your own
generator for anything else, or to aim the inputs:

```csharp
using CsCheck;

// Every decimal the check generates, arguments and constructor parameters alike, now comes from this range.
PledgeProperties.Check<Account>(new PledgePropertyOptions { Iterations = 1_000 }.Use(Gen.Decimal[0m, 10_000m]));
```

The generator registers each contracted member for testing unless every contract kind is
`Ignore`, so set `<PledgeRegistry>true</PledgeRegistry>` in the project under test if your tests
run against a Release build.

For one test per member, use the [xUnit](https://www.nuget.org/packages/Pledge.Testing.Xunit) or
[TUnit](https://www.nuget.org/packages/Pledge.Testing.TUnit) adapter.

## Support

Pledge is MIT-licensed and built in the open. If it saves you a bug or two, consider
[sponsoring its development](https://github.com/sponsors/pagebrooks).

[Source and docs](https://github.com/pagebrooks/pledge) · [Design](https://github.com/pagebrooks/pledge/blob/main/DESIGN.md) · [Issues](https://github.com/pagebrooks/pledge/issues) · [License](https://github.com/pagebrooks/pledge/blob/main/LICENSE)
