# Pledge

Design by Contract for modern .NET: no IL rewriter, no compiler fork, no Visual Studio extension.

Pledge gives C# real postconditions, `Old()` snapshots, invariants and contract inheritance.
Contracts are ordinary C# calls at the top of a method body; a Roslyn source generator lifts
them out at build time and checks them through interceptors. Contract violations are bugs with
blame attached (caller or callee), not validation failures, so FluentValidation and guard
clauses keep their jobs.

> **Status: pre-alpha.** The snippet below works today: `Requires`, `Ensures` with `Result<T>()`
> and `Old()`, and `EnsuresOnThrow` on sync and async methods, checked through the source
> generator and interceptors, with the enforcement modes, blame with file and line, assembly
> metadata and a Native AOT sample. Analyzers, code fixes and the testing bridge are next.
> The full design, semantics and roadmap are in [DESIGN.md](DESIGN.md).

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

Enforcement is an MSBuild property with C++26's vocabulary: `Ignore` (byte-identical IL to a
project without the package, the default in Release), `Observe`, `Enforce` (default in Debug)
and `EnforceFailFast`:

```xml
<PropertyGroup>
  <PledgeMode>Enforce</PledgeMode>
</PropertyGroup>
```

A broken precondition throws `PreconditionViolationException` whose message names the contract,
the member and the call site:

```text
Precondition of Account.Withdraw violated by its caller: insufficient funds [amount <= Balance] (at /src/Program.cs:9)
```

## Repository layout

| Path | Package | Purpose |
| --- | --- | --- |
| `src/Pledge` | `Pledge` | Runtime: the `Pledge` static class (namespace `PledgeContracts`), `PledgeRuntime`, violation exceptions. Ships the generator, analyzers and code fixes in `analyzers/dotnet/cs` and wires `InterceptorsNamespaces` through `build/Pledge.props`. |
| `src/Pledge.Roslyn` | (inside `Pledge`) | Compiler side: the incremental generator and analyzers PLEDGE001-PLEDGE008 in one assembly, since they share the contract-reading code. |
| `src/Pledge.Attributes` | `Pledge.Attributes` | `netstandard2.0` metadata attributes so libraries can carry contract metadata without the runtime. |
| `src/Pledge.Roslyn.CodeFixes` | (inside `Pledge`) | Code fixes, apart from the analyzers because they need Microsoft.CodeAnalysis.Workspaces. |
| `src/Pledge.Testing` | `Pledge.Testing` | Contracts as property-based test oracles, CsCheck backend. |
| `src/Pledge.Testing.Xunit` | `Pledge.Testing.Xunit` | xUnit.net v3 adapter. |
| `src/Pledge.Testing.TUnit` | `Pledge.Testing.TUnit` | TUnit adapter. |
| `tests/` | | xUnit v3 test projects, one per area. `Pledge.Integration.Tests` consumes the generator the way a package consumer does. |
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

## License

MIT. See [LICENSE](LICENSE).
