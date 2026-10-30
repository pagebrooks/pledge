# Pledge

Design by Contract for modern .NET: no IL rewriter, no compiler fork, no Visual Studio extension.

Pledge gives C# real postconditions and `Old()` snapshots, with invariants and contract inheritance
to follow in v1.0.
Contracts are ordinary C# calls at the top of a method body; a Roslyn source generator lifts
them out at build time and checks them through interceptors. Contract violations are bugs with
blame attached (caller or callee), not validation failures, so FluentValidation and guard
clauses keep their jobs.

> **Status: alpha.** [`0.1.0-alpha.1`](https://www.nuget.org/packages/Pledge) is on NuGet, and the
> API can still change between pre-releases. The snippet below works today: `Requires`, `Ensures` with `Result<T>()`
> and `Old()`, and `EnsuresOnThrow` on sync and async methods, checked through the source
> generator and interceptors, with the enforcement modes, blame with file and line, assembly
> metadata and a Native AOT sample. Analyzers PLEDGE001-PLEDGE009 with code fixes catch misplaced
> contracts, misused `Result`/`Old`, impure expressions, mutable `Old` snapshots, constant arguments
> that break a precondition, and calls the contracts do not reach. The testing bridge turns the same contracts into property-based tests
> with CsCheck, through xUnit or TUnit.
> The full design, semantics and roadmap are in [DESIGN.md](DESIGN.md).

```sh
dotnet add package Pledge --prerelease
```

That is the whole setup: the package carries the generator, the analyzers and the build targets
that turn on the interceptors feature.

## What it looks like

```csharp
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

Everything lives in the `PledgeContracts` namespace. The package is `Pledge` and so is the
class; a C# type cannot share its name with a root namespace without `Pledge.Requires(...)`
binding to the namespace instead, so the namespace takes the longer name and is typed once per
file.

Enforcement is an MSBuild property with C++26's vocabulary: `Ignore` (the default in Release),
`Observe`, `Enforce` (the default in Debug) and `EnforceFailFast`. Each kind of contract can
override it:

```xml
<PropertyGroup>
  <PledgeMode>Enforce</PledgeMode>
  <PledgePostconditions>Observe</PledgePostconditions>
</PropertyGroup>
```

`Ignore` compiles to the assembly you would get without Pledge: CI builds a contracted project
in `Ignore` and the same project with the contract lines deleted and no package, and checks the
two are byte-identical. Set `<PledgeMetadata>false</PledgeMetadata>` too if you want that for a
shipped library; by default the contracts are recorded as assembly attributes for tools to read.

A broken precondition throws `PreconditionViolationException` whose message names the contract,
the member and the call site:

```text
Precondition of Account.Withdraw violated by its caller: insufficient funds [amount <= Balance] (at /src/Program.cs:9)
```

## Contracts as property tests

`Pledge.Testing` runs every contracted member of a type as a property test: CsCheck generates the
receiver and arguments, the preconditions discard the inputs they reject, and the postconditions are
the oracle. Nothing is written twice.

```csharp
[Fact]
public void Account_pledges_hold() => PledgeProperties.Check<Account>(iterations: 1_000);

// Or one test per member, with Pledge.Testing.Xunit or Pledge.Testing.TUnit:
[Theory, PledgedMembers(typeof(Account))]
public Task Pledges_hold(PledgeCase pledge) => pledge.CheckAsync();
```

A failure is shrunk to the smallest input and names the contract it broke:

```text
RoundingAccount.Withdraw(decimal) broke its pledge. Postcondition of RoundingAccount.Withdraw violated: Balance == Old(Balance) - amount
Counterexample:
  receiver = new RoundingAccount(542m)
  amount = 0.00000000000000000001701m
Replay this input with Seed = "0VtUVmxeZKh1" and Iterations = 1. CsCheck: 3 shrinks, 38 skipped, 100 total.
```

The generator registers a probe for each contracted member unless every contract kind is `Ignore`,
so a project tested in Release sets `<PledgeRegistry>true</PledgeRegistry>`.

## Repository layout

| Path | Package | Purpose |
| --- | --- | --- |
| `src/Pledge` | `Pledge` | Runtime: the `Pledge` static class (namespace `PledgeContracts`), `PledgeRuntime`, violation exceptions. Ships the generator, analyzers and code fixes in `analyzers/dotnet/cs` and wires `InterceptorsNamespaces` through `build/Pledge.targets`, packed as `buildTransitive` so it also reaches projects that get Pledge through a project reference. |
| `src/Pledge.Roslyn` | (inside `Pledge`) | Compiler side: the incremental generator and analyzers PLEDGE001-PLEDGE009 in one assembly, since they share the contract-reading code. |
| `src/Pledge.Attributes` | `Pledge.Attributes` | `netstandard2.0` metadata attributes so libraries can carry contract metadata without the runtime. |
| `src/Pledge.Roslyn.CodeFixes` | (inside `Pledge`) | Code fixes, apart from the analyzers because they need Microsoft.CodeAnalysis.Workspaces. |
| `src/Pledge.Testing` | `Pledge.Testing` | Contracts as property-based test oracles, CsCheck backend. |
| `src/Pledge.Testing.Xunit` | `Pledge.Testing.Xunit` | xUnit.net v3 adapter. |
| `src/Pledge.Testing.TUnit` | `Pledge.Testing.TUnit` | TUnit adapter. |
| `docs/rules` | | One page per PLEDGE diagnostic, linked from the diagnostics themselves. |
| `tests/` | | Test projects, one per area, on xUnit v3 except the TUnit adapter's. `Pledge.Integration.Tests` consumes the generator the way a package consumer does; `Pledge.Build.Tests` builds throwaway projects with the real SDK to check the mode properties, each mode's behaviour, the byte-identical `Ignore` output and the packed package consumed through a `PackageReference`. `Pledge.Testing.Tests` holds the seeded bug the unit tests miss and the property tests find; `Pledge.Testing.TUnit.Tests` runs under TUnit. |
| `samples/BankAccount` | | The bank-account domain under contracts. |

## Building

The SDK version is pinned in `global.json`.

```sh
dotnet build
dotnet test
dotnet pack
```

Install the git hooks once per clone with [pre-commit](https://pre-commit.com):

```sh
pre-commit install --install-hooks
```

They format staged C# with `dotnet format`, check YAML, JSON and MSBuild XML, validate the GitHub
workflows, and hold commit messages to Conventional Commits. CI runs the same hooks.

Every shipped assembly tracks its public surface in `PublicAPI.Shipped.txt` and
`PublicAPI.Unshipped.txt` next to the project file. A build fails when a public member is added or
removed without a matching edit; apply the fix for new members with:

```sh
dotnet format analyzers Pledge.slnx --diagnostics RS0016 --severity warn
```

Build output goes to `artifacts/` (the .NET artifacts output layout). Assemblies are strong-named
with the checked-in `Pledge.snk`, versions come from git tags, and `samples/BankAccount` is published
with Native AOT in CI as the trim and AOT canary. See [RELEASING.md](RELEASING.md) for how releases
are cut and what they require.

## Packages

| Package | |
| --- | --- |
| [Pledge](https://www.nuget.org/packages/Pledge) | The runtime, generator, analyzers and code fixes. The one most projects need. |
| [Pledge.Attributes](https://www.nuget.org/packages/Pledge.Attributes) | Contract metadata attributes on `netstandard2.0`. |
| [Pledge.Testing](https://www.nuget.org/packages/Pledge.Testing) | Contracts as property-based tests, on CsCheck. |
| [Pledge.Testing.Xunit](https://www.nuget.org/packages/Pledge.Testing.Xunit) | One xUnit.net v3 test per contracted member. |
| [Pledge.Testing.TUnit](https://www.nuget.org/packages/Pledge.Testing.TUnit) | One TUnit test per contracted member. |

Each package's own README, shown on nuget.org, sits next to its project file under `src/`.

## Sponsoring

Pledge is built in the open by one person. If it catches a bug for you, consider
[sponsoring its development](https://github.com/sponsors/pagebrooks).

## License

MIT. See [LICENSE](LICENSE).
