# Pledge

Design by Contract for modern .NET: no IL rewriter, no compiler fork, no Visual Studio extension.

Pledge gives C# real postconditions, `Old()` snapshots, invariants and contract inheritance.
Contracts are ordinary C# calls at the top of a method body; a Roslyn source generator lifts
them out at build time and checks them through interceptors. Contract violations are bugs with
blame attached (caller or callee), not validation failures, so FluentValidation and guard
clauses keep their jobs.

> **Status: pre-alpha skeleton.** The solution builds, the tests run and the packages pack,
> but nothing is implemented yet. The full design, semantics and roadmap are in
> [DESIGN.md](DESIGN.md).

## What it will look like

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

Enforcement is an MSBuild property with C++26's vocabulary: `Ignore` (byte-identical IL to a
project without the package), `Observe`, `Enforce` (default in Debug) and `EnforceFailFast`.

## Repository layout

| Path | Package | Purpose |
| --- | --- | --- |
| `src/Pledge` | `Pledge` | Runtime: the `Pledge` static class, `PledgeRuntime`, violation exceptions. Ships the generator in `analyzers/dotnet/cs` and wires `InterceptorsNamespaces` through `build/Pledge.props`. |
| `src/Pledge.Generator` | (inside `Pledge`) | Incremental source generator: lifts contracts into interceptor wrappers and metadata attributes. |
| `src/Pledge.Attributes` | `Pledge.Attributes` | `netstandard2.0` metadata attributes so libraries can carry contract metadata without the runtime. |
| `src/Pledge.Analyzers` | `Pledge.Analyzers` | Analyzers DBC001-DBC008. Ships the code fixes alongside in `analyzers/dotnet/cs`. |
| `src/Pledge.Analyzers.CodeFixes` | (inside `Pledge.Analyzers`) | Code fixes, in their own assembly because they need Microsoft.CodeAnalysis.Workspaces. |
| `src/Pledge.Testing` | `Pledge.Testing` | Contracts as property-based test oracles, CsCheck backend. |
| `src/Pledge.Testing.Xunit` | `Pledge.Testing.Xunit` | xUnit.net v3 adapter. |
| `src/Pledge.Testing.TUnit` | `Pledge.Testing.TUnit` | TUnit adapter. |
| `tests/` | | xUnit v3 test projects, one per area. |
| `samples/BankAccount` | | The bank-account domain under contracts. |

## Building

The SDK version is pinned in `global.json`.

```sh
dotnet build
dotnet test
dotnet pack
```

Build output goes to `artifacts/` (the .NET artifacts output layout).

## License

MIT. See [LICENSE](LICENSE).
