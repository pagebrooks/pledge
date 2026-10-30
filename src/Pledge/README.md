# Pledge

Design by Contract for .NET 10+: preconditions, postconditions and `Old()` snapshots written as
ordinary C# at the top of a method and checked at build time by a Roslyn source generator. No IL
rewriter, no compiler fork, no IDE extension.

> **Alpha.** The API can still change between pre-releases.

```sh
dotnet add package Pledge --prerelease
```

This package carries everything: the runtime, the generator, the analyzers and their code fixes,
and the build targets that switch on the C# interceptors feature for you. There is no other setup.

## Contracts

```csharp
using PledgeContracts;

public decimal Withdraw(decimal amount)
{
    Pledge.Requires(amount > 0);
    Pledge.Requires(amount <= Balance, "insufficient funds");
    Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
    Pledge.Ensures(Pledge.Result<decimal>() >= 0);
    Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));

    Balance -= amount;
    return Balance;
}
```

The generator checks the contracts at every call to the method in the same project, including
async methods. A broken contract throws an exception that names the contract, the member and the
caller:

```text
Precondition of Account.Withdraw violated by its caller: insufficient funds [amount <= Balance] (at /src/Program.cs:9)
```

## Modes

```xml
<PropertyGroup>
  <PledgeMode>Enforce</PledgeMode>
  <PledgePostconditions>Observe</PledgePostconditions>
</PropertyGroup>
```

| Mode | What happens |
| --- | --- |
| `Ignore` | Nothing is checked and no checking code is generated. Default in Release. |
| `Observe` | Violations go to `PledgeRuntime.ViolationHandler` and execution continues. |
| `Enforce` | Violations throw `PreconditionViolationException` or `PostconditionViolationException`. Default in Debug. |
| `EnforceFailFast` | Violations terminate the process after the handler runs. |

`PledgePreconditions` and `PledgePostconditions` override the mode for one kind of contract.

## Analyzers

The analyzers report misplaced contracts, misused `Result` and `Old`, impure contract
expressions, `Old` snapshots of mutable objects, and constant arguments that break a
precondition (`Withdraw(-5)`), and point out calls the contracts do not reach, such as a call
through an interface. Each diagnostic links to its
[rule page](https://github.com/pagebrooks/pledge/tree/main/docs/rules).

## Limits

Contracts are checked where the method is called, so they run for calls inside the project that
declares them. Calls from other assemblies, through delegates, through an interface or base class,
and by reflection are not checked yet. Contracts are supported on public and internal instance and static
methods of classes, not yet on properties, constructors, generic methods or structs. The analyzers say when a
contract will not run.

## Property tests

[Pledge.Testing](https://www.nuget.org/packages/Pledge.Testing) runs the same contracts as
property-based tests: generated inputs, preconditions as the filter, postconditions as the oracle.

## Support

Pledge is MIT-licensed and built in the open. If it saves you a bug or two, consider
[sponsoring its development](https://github.com/sponsors/pagebrooks).

[Documentation](https://pledge.pagebrooks.com) · [Source](https://github.com/pagebrooks/pledge) · [Design](https://github.com/pagebrooks/pledge/blob/main/DESIGN.md) · [Issues](https://github.com/pagebrooks/pledge/issues) · [License](https://github.com/pagebrooks/pledge/blob/main/LICENSE)
