# Pledge — Design by Contract for .NET: MVP Requirements & Differentiators

Oct 29, 2026 · @Page

## Thesis

Pledge is a free, MIT-licensed Design-by-Contract library for modern .NET that gives C# real postconditions, `old()` snapshots, invariants and contract inheritance, written as ordinary C# and executed by a Roslyn source generator — no IL rewriter, no compiler fork, no Visual Studio extension required.

**Who it is for.** Teams writing C# where a wrong result costs more than a slow build: finance, healthcare, defense, infrastructure libraries, and anyone reviewing AI-generated code who wants machine-checkable statements of what a method must do.

**Why it should exist.** .NET had this (Code Contracts, 2008–2017) and lost it to a brittle mechanism, not to a lack of demand. C++26 just standardized contracts (March 2026) with a design .NET can borrow wholesale. The only living .NET option, Metalama.Patterns.Contracts, covers value constraints (`[NotNull]`, `[Range]`) but not relational postconditions, and runs on a Roslyn fork. Nothing since `ccrewrite` lets a C# developer write `Ensures(Balance == Old(Balance) - amount)` and have it checked.

**What it is not.** It is not an input-validation framework. Contract violations are bugs with blame attached (caller or callee); validation failures are expected runtime conditions. FluentValidation, DataAnnotations and guard clauses keep their jobs.

## Landscape

No current package does expression postconditions on .NET; the only living contracts product (Metalama) does value-constraint attributes, and everything else is preconditions or null-only.

| Option | Status | Mechanism | Pre | Post | Invariants | `old()` | Inheritance | Gap it leaves |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [Code Contracts](https://github.com/microsoft/CodeContracts/issues/409) (Microsoft Research) | Dead; no .NET Core support, repo defunded \~2017 | `ccrewrite` IL rewriter + separate static checker | Expression | Expression | Yes | Yes | Yes | The whole feature set, with a mechanism that broke on each compiler release |
| [Metalama.Patterns.Contracts](https://doc.postsharp.net/metalama/patterns/contracts) (PostSharp) | Active; MIT since 2025.1, v2026.1; IDE tooling commercial | Aspects on a Roslyn fork (Metalama.Compiler) | Attribute vocabulary (`[NotNull]`, `[Range]`, `[Regex]`…) | Same vocabulary on return / out / getters | Method run after each public method | No | No | Relational postconditions, `old()`, inheritance rules; requires their compiler fork |
| [NullGuard.Fody](https://github.com/Fody/NullGuard) / RuntimeNullables.Fody | Active, small | Fody IL weaving | Null only | Null only on returns, out params, task results | No | No | No | Anything beyond nullness |
| [MethodBoundaryAspect.Fody](https://github.com/vescon/MethodBoundaryAspect.Fody) | Active, small | Fody entry/exit hooks | Raw material | Raw material | No | No | No | Not a contracts library; a toolkit one could be built on |
| Guard libraries: Ardalis.GuardClauses, CommunityToolkit `Guard`, Throw, [IOKode Ensuring](https://www.nuget.org/packages/IOKode.OpinionatedFramework.Generators.Ensuring) | Active | Imperative calls in the body | Yes | Only as an `if` at the bottom of your own method | No | No | No | Nothing declarative, nothing visible to callers |
| BCL: NRT annotations, `ArgumentNullException.ThrowIfNull`, `[return: NotNull]` | Active, first-party | Compiler flow analysis + helpers | Null only, declarative | Null only, declarative | No | No | No | Microsoft did the null case properly and stopped |
| JetBrains `[ContractAnnotation]` | Active | Static only, ReSharper/Rider | Hints | Hints | No | No | No | No runtime check at all |
| Vogen, value objects | Active | Source generator | — | — | By construction, immutable only | — | — | Mutable types |
| CsCheck, FsCheck | Active | Property-based testing | — | Properties written by hand | — | — | — | Not attached to the method; no reuse of the contract |
| Dafny | Active (Microsoft Research / AWS) | Verified language compiling to C# | Proven | Proven | Proven | Yes | Yes | A different language, not contracts on C# |

**Language-level attempts.** Stephen Toub's 2015 [Method Contracts proposal](https://github.com/dotnet/roslyn/issues/119) (`requires` / `ensures` / `return`) drew 259 comments and was closed; its csharplang successor [#105](https://github.com/dotnet/csharplang/issues/105) is open but the team has been unenthusiastic. The authoring API below stays close to that proposal's vocabulary so the library reads as a prototype of the language feature, not a competitor to it.

**External reference design.** C++26 (completed March 2026) ships `pre`, `post`, `contract_assert` and four evaluation semantics — ignore, observe, enforce, quick-enforce — as a deliberate MVP, with virtual-function support deferred to C++29 ([P3850](https://www.open-std.org/jtc1/sc22/wg21/docs/papers/2026/p3850r1.pdf)). This document borrows that vocabulary so .NET developers who follow C++ recognize it.

## Design principles

Every principle below is the inverse of a specific way Code Contracts died or a specific objection teams raise to Metalama.

1. **Pure Roslyn, no IL, no fork.** Source generators, interceptors and analyzers only. No post-build rewriter in the critical path, no custom compiler. If a future C# version breaks something, it breaks a generator, which is a package update, not a toolchain migration.
2. **Source is the single truth.** Contracts are written as C# in the method they describe. The generator derives everything else — runtime checks, metadata attributes, test oracles, docs — from that one place. Nothing is authored twice.
3. **Degrade safely, never silently lie.** If the generator is absent, contract calls compile to no-ops and an analyzer warning says so. The `Contract.Requires` booby trap (process terminates asking for a rewriter that doesn't support your runtime) must be impossible to reproduce.
4. **Zero cost when off.** In `Ignore` mode the generator emits nothing and the no-op calls are removed by `[Conditional]`. Release builds pay nothing unless the team opts in.
5. **NativeAOT and trimming safe.** No reflection, no `Expression<T>` compilation, no runtime code generation. Everything the checks need is emitted at compile time.
6. **Contracts are not validation.** A violation is a bug with blame: `PreconditionViolationException` blames the caller, `PostconditionViolationException` and `InvariantViolationException` blame the callee. The exception types, messages and docs say so. The library never positions itself against FluentValidation.
7. **Borrow C++26's vocabulary, not Code Contracts' reputation.** Enforcement modes are ignore / observe / enforce. The package and its static class are not named "Code Contracts" and do not live in `System.Diagnostics.Contracts`, so the two can coexist and search results don't inherit the old project's obituaries.
8. **Stay close to the language proposal.** Where csharplang #105 and C++26 agree on a semantic (fail-fast vs throw is configurable; `old()` is explicit; postconditions see the result), follow them. If Microsoft ever ships contracts, migration should be mechanical.
9. **No telemetry, no phone-home, no sponsorship nag in the build.** MIT, forever. The Moq lesson is permanent in this ecosystem.
10. **One install, no setup.** `dotnet add package` wires `InterceptorsNamespaces` through build props; an analyzer with a code fix catches the one case where it can't.

## Authoring model

Contracts are ordinary C# calls at the top of the method body; the IDE, compiler and refactoring tools treat them as code, and the generator lifts them out at build time.

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

[Invariant]
private void BalanceNeverNegative() => Pledge.Invariant(Balance >= 0);
```

**MVP surface (static class `Pledge`, namespace `PledgeContracts`):**

| Member | Meaning | Notes |
| --- | --- | --- |
| `Requires(bool condition, string? message = null)` | Precondition; blames the caller | Message auto-filled from the expression text via `[CallerArgumentExpression]` when omitted |
| `Ensures(bool condition, string? message = null)` | Postcondition on normal return | May reference `Result<T>()`, `Old(...)`, parameters, fields |
| `Result<T>()` | The return value inside an `Ensures` | Only legal in postconditions; type must match the method's return type (analyzer) |
| `Old<T>(T expression)` | Value of the expression at method entry | Snapshot is taken before the body runs; expression must be side-effect free (analyzer) |
| `EnsuresOnThrow<TException>(bool condition)` | Postcondition when the method exits via `TException` or a subtype | C++26 has no equivalent; Code Contracts did and it is useful for transactional invariants |
| `ValueAtReturn(out T param)` | Final value of an `out` parameter in a postcondition | v1.0 |
| `Invariant(bool condition)` | Object invariant, inside a method marked `[Invariant]` | Checked after every public method and setter completes (decorator and weaver modes); v1.0 |

**Placement rules (enforced by analyzer):**

- Pledge calls must be the first statements of the body, before any other code. Anything after the first non-contract statement is a diagnostic, not silently ignored.
- `Requires` before `Ensures` before `EnsuresOnThrow`, by convention; the analyzer offers a fix to reorder.
- Contract expressions must be pure: no assignments, no `await`, no calls to methods not marked `[Pure]` or known-pure BCL members. Violations are a warning in MVP, an error in v1.0.
- Supported targets in MVP: instance and static methods (sync and `async Task` / `Task<T>` / `ValueTask<T>`), property getters and setters. Constructors, iterators, local functions, lambdas and `ref` returns are v1.0 or later.

**Interface and abstract contracts (v1.0).** Interface members have no body, so their contracts live in a sibling class marked `[PledgeFor(typeof(IAccount))]` with mirroring signatures, following the Code Contracts and csharplang #105 pattern. Implementations inherit them automatically in decorator mode.

**Why calls and not attributes.** Attributes cannot carry lambdas or reference locals, which is why Metalama's vocabulary is limited to fixed predicates. Calls get full language support for free. The generator also emits attribute metadata (see Metadata and documentation) so the declarative view exists for consumers without the authoring cost.

## Execution model

One authoring model, three execution strategies; the MVP ships the first, v1.0 adds the second, and the third stays an opt-in package that the core never depends on.

&#91;embedded content: architecture · one generator, three execution paths, two side outputs\]

The generator is the only component that reads the source; everything to its right and below is derived from the lifted predicates, so the three execution paths and the test bridge can never disagree about what a contract says.

| Strategy | How checks run | Covers | Does not cover | Ships in |
| --- | --- | --- | --- | --- |
| **Interceptors** (caller-side) | Generator emits a checked shadow method per contracted method and an `[InterceptsLocation]` for every call site in the compilation; the callee is untouched | All direct calls inside the compiling assembly; async via `await` in the wrapper | Delegates, reflection, calls from other assemblies, virtual dispatch to a derived override | MVP |
| **Generated decorators** (interface-side) | For each contracted interface or `[Pledged]` class, generate `Foo__Pledged : IFoo` wrapping every member; register as a DI decorator | Everything behind an interface, across assemblies; contract inheritance; invariants | Concrete types not behind an interface | v1.0 |
| **IL weaver** (callee-side, optional) | Separate Fody add-in injecting prologue/epilogue into the method itself | Every caller, including ones you never compile | Adds a build-time weaving step; the one road that has died before | Later, separate package |

**Generated code shape (interceptor mode, sync):**

```csharp
// <auto-generated/>
file static class Account__Pledges
{
    [InterceptsLocation(version: 1, data: "...")]
    public static decimal Withdraw(this Account @this, decimal amount)
    {
        if (!(amount > 0))
            PledgeRuntime.Violation(PledgeKind.Precondition, "amount > 0", nameof(Account.Withdraw));
        var old_Balance = @this.Balance;
        decimal __result;
        try { __result = @this.Withdraw(amount); }
        catch (InvalidOperationException) when (PledgeRuntime.CheckOnThrow(@this.Balance == old_Balance, ...)) { throw; }
        if (!(@this.Balance == old_Balance - amount))
            PledgeRuntime.Violation(PledgeKind.Postcondition, "Balance == old(Balance) - amount", ...);
        if (!(__result >= 0))
            PledgeRuntime.Violation(PledgeKind.Postcondition, "Result >= 0", ...);
        return __result;
    }
}
```

The body's original `Pledge.*` calls remain in source and compile away (`[Conditional("PLEDGE_NEVER")]`), so the intercepted callee carries no checks and no cost.

**Enforcement modes** — an MSBuild property, defaulting to `Enforce` in Debug and `Ignore` in Release, overridable per project and per kind:

| Mode | Behavior | Cost | C++26 equivalent |
| --- | --- | --- | --- |
| `Ignore` | Generator emits no wrappers; calls compile away | Zero | ignore |
| `Observe` | Checks run; violations go to `PledgeRuntime.ViolationHandler` and execution continues | Checks + handler | observe |
| `Enforce` | Checks run; violations throw the kind-specific exception | Checks | enforce |
| `EnforceFailFast` | Checks run; violations call `Environment.FailFast` after the handler | Checks | quick-enforce |

```xml
<PropertyGroup>
  <PledgeMode>Enforce</PledgeMode>
  <PledgePreconditions>Enforce</PledgePreconditions>
  <PledgePostconditions>Observe</PledgePostconditions>
  <PledgeInvariants>Ignore</PledgeInvariants>
</PropertyGroup>
```

**Violation handler.** `PledgeRuntime.ViolationHandler` is a static delegate receiving a `PledgeViolation` record (kind, expression text, member, blame, caller file/line from `[CallerFilePath]`/`[CallerLineNumber]` captured at generation time). Default in `Enforce` throws; teams wire it to logging in `Observe`. This is the C++26 `handle_contract_violation` idea and it is what makes `Observe` useful for rolling contracts into an existing codebase without breaking it.

**Exceptions.** `BrokenPledgeException` (abstract base) with `PreconditionViolationException`, `PostconditionViolationException` and `InvariantViolationException`. Preconditions can optionally map to `ArgumentException` subtypes for public API surfaces via `Pledge.Requires<TException>(...)`, which is the compromise the csharplang thread settled on after the fail-fast debate.

## Semantics

The rules below are the ones Code Contracts documented and C++26 debated; getting them specified before code matters more than any feature.

**Evaluation order.** Preconditions in source order, then `Old` snapshots (in source order of first appearance), then the body, then postconditions in source order. A failing precondition short-circuits: the body never runs, no snapshots are taken.

**`Old()` snapshots.** Taken once, after preconditions pass and before the body. Reference types snapshot the reference, not a deep copy — the docs say so loudly, and an analyzer warns when `Old` wraps a mutable reference type without a `.ToArray()` / `.ToList()` / record-with-expression. In `Ignore` mode no snapshot code is emitted.

**Postconditions and exceptions.** `Ensures` is checked only on normal return. `EnsuresOnThrow<T>` is checked in a `catch when` filter for `T` and its subtypes, and never swallows: the original exception always propagates. A postcondition that itself throws is reported as a postcondition violation with the inner exception attached.

**Async.** For `Task`, `Task<T>` and `ValueTask<T>` the wrapper awaits the callee and checks postconditions after completion; `Result<T>()` is the awaited value. Snapshots are taken before the first `await`. Cancellation (`OperationCanceledException`) bypasses postconditions by default; configurable.

**`void` and `out`.** `Result<T>()` in a `void` method is an analyzer error. `out` parameters are referenced through `ValueAtReturn(out x)` (v1.0).

**Properties.** Getter contracts may use `Result<T>()`; setter contracts may reference `value`. Auto-properties cannot carry contracts (no body); the analyzer suggests converting.

**Purity.** Contract expressions must not assign, `await`, call `Console`/IO, or invoke methods lacking `[Pure]` (either `System.Diagnostics.Contracts.PureAttribute` or the library's own). Known-pure BCL members (`string.Length`, LINQ `Any`/`All`/`Count`, `Math.*`, comparison operators) are allow-listed. Impure contracts are the classic source of heisenbugs when modes change.

**Inheritance (v1.0, Eiffel/Liskov rules, enforced where the generator sees both ends):**

| Relationship | Preconditions | Postconditions | Invariants |
| --- | --- | --- | --- |
| Override of a virtual/abstract member | May not add new ones (analyzer error); inherits the base's | Base's AND override's both checked | Class invariants of base AND derived |
| Implementation of an interface member | Inherits the interface's `[PledgeFor]` set; may not add | Interface's AND implementation's | — |
| Explicit weakening (rare) | `Requires` in the override with `[Weakens]` is allowed and OR-ed with the base | — | — |

**Generics and value types.** Contracts on generic methods are lifted per instantiation site; `Old` of a struct copies it (true snapshot). Nullable value types compare with the usual `==` semantics.

**Re-entrancy and recursion.** Checks run on every call including recursive ones. Invariants are suspended while a public method of the same instance is already on the stack (Code Contracts' rule), to avoid checking half-updated state.

**Thread safety.** The library adds no synchronization. A contract that reads mutable shared state races exactly as the body would; the docs say so.

## Static layer: analyzers and code fixes

The analyzer is what makes the library feel like a language feature instead of a helper class; every diagnostic below ships with a code fix where one is mechanical.

| ID | Severity | Rule | Fix | Ships in |
| --- | --- | --- | --- | --- |
| DBC001 | Error | Contract call after a non-contract statement | Move to top | MVP |
| DBC002 | Error | `Result<T>()` outside `Ensures`, or in a `void` method, or `T` ≠ return type | — | MVP |
| DBC003 | Error | `Old(...)` outside a postcondition | — | MVP |
| DBC004 | Warning | Impure expression in a contract (assignment, `await`, non-`[Pure]` call) | — | MVP |
| DBC005 | Warning | Generator not active: contracts compile to no-ops (missing package reference, `InterceptorsNamespaces` not set, unsupported language version) | Add build property | MVP |
| DBC006 | Info | Contracted method has no interceptable call sites in this compilation (checks will never run here) | Suggest decorator mode | MVP |
| DBC007 | Warning | `Old` of a mutable reference type without an explicit copy | Wrap in `.ToArray()` etc. | MVP |
| DBC008 | Warning | Call site passes a constant that violates a precondition (`Withdraw(-5)`) | — | MVP (constants only) |
| DBC010 | Error | Override or interface implementation adds a precondition the base lacks | Remove or mark `[Weakens]` | v1.0 |
| DBC011 | Warning | Override's postcondition is weaker than base (heuristic: base `Ensures` not implied) | — | v1.0 |
| DBC012 | Warning | Public method on a type with invariants is `[DoNotCheckInvariants]` without comment | — | v1.0 |
| DBC013 | Info | Precondition duplicates an NRT or `ThrowIfNull` check | Remove duplicate | v1.0 |
| DBC020 | Warning | Contract references a parameter that is reassigned in the body before return (postcondition sees the new value) | — | v1.0 |

**Later (not scoped):** SMT-backed checking of call sites against preconditions using Z3 via the analyzer host; range analysis for numeric preconditions; contract-aware nullability flow. These are the Clousot features from Code Contracts' static checker and they are explicitly deferred so the runtime story ships first.

## Testing bridge: contracts as property-based test oracles

This is the differentiator no competitor has and the one most worth leading with: every contracted method becomes a free property-based test.

Because the generator lifts each contract into real, callable predicates — `Account__Pledges.Withdraw_Pre(account, amount)` and `Withdraw_Post(account, amount, old_Balance, result)` — it can also emit a registry of pledged members. A companion package (`Pledge.Testing`) walks that registry and runs a property test per member:

1. Generate instances of the receiver and arguments with CsCheck (or FsCheck) generators, user-overridable per type.
2. Discard samples that fail the preconditions (preconditions are the generator's filter, not a failure).
3. Call the method.
4. Assert the postconditions; on failure, shrink and report the minimal counterexample with the contract text.

```csharp
[Fact]
public void Account_pledges_hold() =>
    PledgeProperties.Check<Account>(iterations: 1_000);
```

**What this buys:**

- A postcondition written once is checked at runtime in Debug *and* fuzzed in CI, with no second expression to keep in sync.
- Invariants get hammered across random public-method sequences (stateful testing, v1.0), which is where real invariant bugs live.
- It gives the "AI wrote the body, the contract says what it must do" workflow a concrete, automated shape.

**Scope.** MVP: xUnit and TUnit adapters, CsCheck backend, stateless methods with primitive and record arguments. v1.0: FsCheck backend, stateful sequences for invariants, custom generators via attribute, snapshot of counterexamples to a file for regression.

## Metadata and documentation

The generator emits a declarative view of every contract so consumers in other assemblies, doc tools and future analyzers can see it without running it.

- **Metadata attributes.** For each contracted member the generator adds `[Requires("amount > 0")]`, `[Ensures("Balance == old(Balance) - amount")]`, `[EnsuresOnThrow(typeof(InvalidOperationException), "...")]` with the normalized expression text. These are the only strings in the system and they are output, never input. They survive into the compiled assembly and NuGet package.
- **XML doc enrichment.** An MSBuild task appends a `<contracts>` block (or `<remarks>` text, configurable) to the member's XML doc so IntelliSense shows *Requires: amount > 0 · Ensures: result ≥ 0* on hover and DocFX renders it. Code Contracts' doc generator did this and users missed it most.
- **Cross-assembly consumption (v1.0).** The analyzer reads the metadata attributes from referenced assemblies, so DBC008 (constant violates precondition) works against a contracted library you consume, not only code in your solution.
- **Markdown export (later).** `dotnet contracts export` writes a per-type contract summary for READMEs and API docs.

## Developer experience and performance budget

The install experience and the overhead numbers are launch content, not afterthoughts; both get measured and published.

**Install.** One package reference. The package's `build/*.props` appends the generator's namespace to `InterceptorsNamespaces` and sets `LangVersion` guidance; DBC005 catches the cases it can't fix (older SDKs, a project that overrides the property). Target: zero manual steps on .NET 10+ with the .NET 10+ SDK.

**Supported matrix (MVP).** Runtime `net10.0`+ for the runtime package (`netstandard2.0` for the attributes-only package so libraries can carry metadata without taking a runtime dependency); SDK 10+ for the generator; C# 14+. .NET 8 leaves support in November 2026 and .NET 9 already has, so a lower floor buys nothing at launch. The interceptor API surface is wrapped behind one internal abstraction so a change to the experimental feature is a one-file fix.

**Performance budget, published with every release:**

| Scenario | Target |
| --- | --- |
| `Ignore` mode | Byte-identical IL to a project without the package, verified in CI by comparing assemblies |
| `Enforce`, precondition only | ≤ cost of the equivalent hand-written `if` + throw; no allocation |
| `Enforce`, postcondition with one `Old` of a value type | One local; no allocation |
| `Enforce`, async method | One additional state machine (the wrapper); measured against a hand-written wrapper |
| Build time | Generator ≤ 50 ms incremental on a 1,000-method project; uses incremental generator pipeline with proper equatable models |
| NativeAOT | Sample app publishes with zero trim/AOT warnings attributable to the library |

**Debuggability.** Generated wrappers carry `#line` directives pointing violations at the contract's source line, and `[DebuggerStepThrough]` on the plumbing so F11 lands in the user's method, not the shadow. Exception messages show the contract text, the member, and for preconditions the caller's file:line.

**Templates and samples.** A `dotnet new` item template for a contracted service, a sample repo with a bank-account domain under contracts + the property-test bridge + a NativeAOT publish, and a BenchmarkDotNet project that produces the table above.

## Scope: MVP, v1.0, later

The MVP is the smallest thing that proves the authoring experience holds up in real code and that nobody else has: expression postconditions with `Old`, checked via interceptors, plus the property-test bridge.

| Area | MVP (v0.1–0.3) | v1.0 | Later |
| --- | --- | --- | --- |
| Authoring | `Requires`, `Ensures`, `Result<T>`, `Old`, `EnsuresOnThrow` | `ValueAtReturn`, `Invariant` + `[Invariant]`, `[PledgeFor]` interface contracts, `Requires<TException>` | Constructors, iterators, local functions, lambdas, `ref` returns |
| Targets | Instance/static methods, sync + `Task`/`Task<T>`/`ValueTask<T>`, property get/set | Generic methods per instantiation, records, structs | `IAsyncEnumerable`, `ref struct` members |
| Execution | Interceptor mode | Generated DI decorators for interfaces; contract inheritance | Optional Fody weaver package (callee-side) |
| Modes | `Ignore` / `Observe` / `Enforce` / `EnforceFailFast`, per-kind MSBuild properties | Per-assembly and per-type overrides via attributes | Runtime toggle for `Observe` sampling |
| Analyzers | DBC001–008 | DBC010–020, cross-assembly metadata reading | SMT/range checking |
| Metadata | `[Requires]`/`[Ensures]` text attributes | XML doc enrichment task | Markdown export CLI |
| Testing bridge | Registry + CsCheck + xUnit/TUnit, stateless | FsCheck backend, stateful invariant sequences, counterexample snapshots | Coverage report: which contracts the test run exercised |
| DX | Auto-wired build props, DBC005, `#line` mapping, exception messages with contract text | `dotnet new` template, sample repo, benchmark suite published | Rider/VS tooltips via the metadata attributes |
| Platforms | net10.0+, SDK 10+, C# 14+, NativeAOT clean | netstandard2.0 attributes-only package | — |

**MVP exit criteria.** (1) The bank-account sample runs under `Enforce` with every contract exercised; (2) `Ignore` mode produces byte-identical IL; (3) NativeAOT publish is warning-free; (4) the property-test bridge finds a seeded bug the unit tests miss; (5) three external projects try it and the DBC005 setup diagnostic fires zero times in anger.

## Differentiators

Six things no current option offers together; the first three are the headline, the rest are why teams stay.

| Capability | This package | Metalama.Patterns.Contracts | Guard libraries | Code Contracts (historical) |
| --- | --- | --- | --- | --- |
| Expression postconditions (`Ensures(Balance == Old(Balance) - amount)`) | Yes | No — fixed attribute predicates on values | No | Yes |
| `Old()` snapshots | Yes | No | No | Yes |
| Contracts become property-based tests automatically | Yes | No | No | No |
| Contract inheritance (Liskov rules enforced) | v1.0 | No | No | Yes |
| Mechanism | Pure Roslyn: generator + interceptors + analyzers | Aspects on a Roslyn compiler fork | Plain calls | IL rewriter + VS extension |
| Enforcement modes | ignore / observe / enforce / fail-fast, per kind, C++26 vocabulary | Enabled/disabled per kind | Always on | Several, via rewriter switches |
| Zero cost when off | Byte-identical IL, verified | Aspect not applied | N/A | Rewriter removes |
| NativeAOT / trimming | Clean by design | Supported | Yes | N/A |
| Metadata visible to callers and docs | Text attributes + XML doc enrichment | Attributes (the predicates themselves) | None | Doc generator |
| License | MIT, no commercial tier planned for the core | MIT core since 2025; IDE tooling commercial | MIT | MIT (dead) |
| Setup | One package reference | Package + Metalama compiler | One package | Separate installer + VS extension |
| Blame semantics in exception types | Pre / Post / Invariant exceptions | Pre: `ArgumentException`; Post: `PostconditionViolationException` | `ArgumentException` only | Configurable |

**Positioning sentence.** *Pledge is Design by Contract for C#, the way C++26 just did it: write `Requires` and `Ensures` as ordinary code, get them checked in Debug, fuzzed in CI and documented in IntelliSense — with no rewriter, no compiler fork and no cost in Release.*

**Where overlap is acceptable.** Value-constraint preconditions (`[NotNull]`, ranges) overlap with Metalama and the BCL; this package does them because a contracts library that can't say `amount > 0` is absurd, but it does not compete on breadth of validator attributes. Teams can use Metalama's vocabulary and this package's postconditions side by side.

## Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Interceptors change or are cut (still listed experimental in Roslyn) | Medium | High | Isolate behind one internal abstraction; decorator mode is a full fallback; EF and ASP.NET ship on interceptors, so breakage would hit Microsoft first |
| Microsoft adds language contracts (C++26 may reopen csharplang #105) | Low–medium over 3 years | Medium | Mirror the proposal's vocabulary so migration is mechanical; position as the reference implementation the LDM can study; a language feature would still need the testing bridge and docs tooling |
| Caller-side-only coverage surprises users ("my contract didn't fire") | High | Medium | DBC006 warns when a contracted method has no interceptable callers; docs lead with the coverage table; decorator mode in v1.0; weaver package later |
| `Old()` cost or reference-semantics confusion | Medium | Medium | DBC007; snapshots only in enabled modes; docs example on the first page |
| Impure contract expressions cause mode-dependent behavior | Medium | High | DBC004 from day one; allow-list of pure BCL members; escalate to error in v1.0 |
| Code Contracts PTSD: the community assumes "rewriter, slow, abandoned" | High | High | Never use the name; first README line says no IL, no fork, no VS extension; publish the byte-identical-IL CI check and the NativeAOT sample |
| Metalama adds expression postconditions | Low–medium | Medium | They are constrained by attribute-based authoring on a compiler fork; the pure-Roslyn and PBT-bridge story stands regardless; collaboration is also an option |
| Solo-maintainer bus factor deters enterprise adoption | Medium | Medium | Small, well-specified surface; this document as the spec; CI that proves the invariants above; accept early co-maintainers |
| Generator performance on large solutions | Medium | Medium | Incremental generator with equatable models from the first commit; benchmark gate at 1,000 methods |
| Trust (Moq precedent) | Low | Fatal if it happens | MIT, no telemetry, no build-time nag, reproducible builds, signed packages |

## Non-goals

- **Input validation.** No validator vocabulary beyond what contracts need, no model binding integration, no localization of messages for end users. FluentValidation and DataAnnotations stay.
- **Static verification in MVP.** No proof, no SMT, no range analysis until the runtime story is solid; DBC008 handles constants only.
- **Runtime proxies.** No Castle DynamicProxy, no reflection-based interception; everything is compile time.
- **Replacing NRT.** Null contracts are supported but the docs steer users to NRT for nullness and to this package for everything else.
- **A commercial tier for the core.** If money is ever involved it is tooling or support around the package, never features withheld from it.
- **Supporting .NET Framework, runtimes before .NET 10, or pre-C# 14 compilers.** The interceptor dependency and the support lifecycle make this impractical; the attributes-only package targets `netstandard2.0` so metadata can flow through older libraries.

## Open questions and naming

Decisions that change the API shape or the pitch and should be settled before the first public commit.

**Decided (Oct 29, 2026): the name is Pledge.** Static class `Pledge`, namespace `PledgeContracts`. The class cannot share its name with a root namespace: C# binds a simple name to a namespace member before it consults using directives, so a `Pledge` namespace anywhere in a compilation would make `Pledge.Requires(...)` fail with CS0234. The plural-namespace, singular-class shape is the BCL's own (`System.Diagnostics.Contracts.Contract`, `System.Threading.Tasks.Task`). Consequences: no shipped assembly declares a namespace rooted at `Pledge` (a test enforces this), the generator emits into `PledgeContracts.Generated`, and package IDs keep the `Pledge` prefix while namespaces take `PledgeContracts` (`PledgeContracts.Testing`, `PledgeContracts.Testing.Xunit`, ...). Packages `Pledge` (runtime, generator, analyzers and code fixes; the Roslyn components are one `Pledge.Roslyn` assembly plus a `Pledge.Roslyn.CodeFixes` assembly, both under `analyzers/dotnet/cs`), `Pledge.Testing` with `Pledge.Testing.Xunit` and `Pledge.Testing.TUnit` adapters, `Pledge.Attributes` (`netstandard2.0`). Base exception `BrokenPledgeException`. Analyzer IDs keep the `DBC` prefix, since they name the concept rather than the brand. Rejected: `Contract` (BCL collision and the ccrewrite trap), Vow/Avow (vow-lang is a contracts-first language for AI agents in the same niche), Pact (contract testing), Covenant (.NET C2 framework), Bond (Microsoft Bond), Oath (sounds like OAuth).

- [ ] **Static class name (resolved above; reasoning kept for the record).** `Contract.*` reads naturally and matches csharplang #105, but collides with `System.Diagnostics.Contracts.Contract` for anyone who imports both — and that class carries the ccrewrite booby trap. Options: keep `Contract` in a distinct namespace and ship an analyzer that flags the BCL one; or use a distinct short name (`Dbc.Requires`, `Spec.Ensures`). Recommendation: distinct name; the BCL collision is a support burden forever.
- [ ] **Fail-fast vs throw default.** The csharplang thread fought this for years; C++26 made it a build mode. Recommendation: throw by default in `Enforce`, offer `EnforceFailFast`, document when each is right.
- [ ] **Precondition exception mapping for public APIs.** Does `Requires(x != null)` throw `PreconditionViolationException` or `ArgumentNullException`? Recommendation: the former by default, `Requires<ArgumentNullException>` opt-in, plus DBC013 steering people to `ThrowIfNull` for the null case.
- [ ] **Interceptor abstraction boundary.** How much of the interceptor plumbing to hide so decorator mode can share the lifted predicates — settle before writing the generator.
- [ ] **`Old()` of reference types.** Shallow reference by default with DBC007, or refuse without an explicit copy? Recommendation: allow + warn in MVP; revisit with user feedback.
- [ ] **Async cancellation semantics.** Skip postconditions on `OperationCanceledException` by default, or treat cancellation as a normal exit?
- [x] **Minimum C# version.** Decided (Oct 29, 2026): C# 14 on the .NET 10 SDK, runtime floor `net10.0`. .NET 8 leaves support in November 2026 and .NET 9 already has, so 13 would not widen adoption at launch.
- [ ] **Scope of the attributes-only package.** Exactly which attributes ship in the `netstandard2.0` package so libraries can carry contract metadata without the runtime.

**Names considered** (before publishing, confirm with `dotnet package search Pledge --exact-match`, the GitHub org and the domain):

| Candidate | Outcome | Notes |
| --- | --- | --- |
| **Pledge** | **Chosen** | One syllable, unambiguous aloud, `BrokenPledgeException`; only collisions are OpenBSD `pledge(2)` and fundraising software |
| Vow / Avow | Rejected | [vow-lang.com](https://vow-lang.com) is a verified language with `requires`/`ensures`/`invariant` and blame semantics — the same niche and the same AI-code pitch |
| Proviso | Runner-up | Exact meaning (a condition attached to an agreement), no collisions; three syllables and a spelling people fumble |
| Treaty | Runner-up | Two-party framing; `TreatyViolationException` reads naturally |
| Swear | Considered | Funniest option; SEO and corporate-filter risk |
| Stipulate | Considered | Serious tone; forces a verb-only API shape |
| Contract | Rejected | `System.Diagnostics.Contracts.Contract` collision and the ccrewrite booby trap |
| Pact, Covenant, Bond, Oath, Vouch, FinePrint | Rejected | Existing developer-space projects or products, or confusable aloud |

## Launch plan: building the following

The package is the content; each milestone below is both a release and a post that .NET developers will share without being sold anything.

1. **Spec first, in public.** Publish this document (trimmed) as the repo's `DESIGN.md` before code, and post it to r/dotnet and the csharplang #105 thread asking for holes. Early critics become early users.
2. **MVP release + "Code Contracts died for a reason; here's the version that can't"** — the retrospective post: what killed ccrewrite, what C++26 did, what interceptors change. Show HN, r/csharp, Lobsters.
3. **The byte-identical-IL proof** — a short post with the CI check and the NativeAOT publish. This is the credibility post for architects.
4. **The property-test bridge demo** — seed a bug in the bank-account sample that unit tests miss and let the contracts find it. This is the post that gets shared.
5. **"Contracts for AI-written code"** — the workflow post: you write `Requires`/`Ensures`, the model writes the body, the bridge checks it. Timely, and the one most likely to escape the .NET bubble.
6. **Conference talks** — NDC, .NET Conf community track, DevSum/Update: "What C++26 contracts mean for C#." The talk is the spec with a demo.
7. **Interop with the ecosystem** — PR a `[Pure]` allow-list entry for popular libraries, a CsCheck integration upstream, a Metalama compatibility note. Each is a mention in someone else's changelog.

**Success metrics for the first six months:** 500 GitHub stars; three unaffiliated projects using it in CI; one external contributor landing an analyzer rule; the csharplang thread referencing it; zero DBC005 reports from the setup path.

## Sources

- [Microsoft/CodeContracts issue #409 — project status and .NET Core](https://github.com/microsoft/CodeContracts/issues/409)
- [Code Contracts documentation (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/framework/debug-trace-profile/code-contracts)
- [Maintaining ccrewrite against Roslyn (Habr)](https://habr.com/en/post/263235/)
- [dotnet/roslyn #119 — Method Contracts proposal (2015)](https://github.com/dotnet/roslyn/issues/119) · [dotnet/csharplang #105](https://github.com/dotnet/csharplang/issues/105)
- [Roslyn Language Feature Status (interceptors: experimental)](https://github.com/dotnet/roslyn/blob/main/docs/Language%20Feature%20Status.md)
- [WG21 P3850 — contracts after C++26](https://www.open-std.org/jtc1/sc22/wg21/docs/papers/2026/p3850r1.pdf) · [C++26 contract modes](https://wrocpp.github.io/toolset/contracts-2026/)
- [Metalama Contracts documentation](https://doc.postsharp.net/metalama/patterns/contracts) · [Metalama.Patterns.Contracts on NuGet](https://feed.nuget.org/packages/Metalama.Patterns.Contracts/) · [Metalama 2025.1 licensing](https://blog.postsharp.net/metalama-2025-1)
- [NullGuard.Fody](https://github.com/Fody/NullGuard) · [MethodBoundaryAspect.Fody](https://github.com/vescon/MethodBoundaryAspect.Fody/commit/b602bdf835da08bdd5edeb158b9aba6d1d8abdc5)
- [IOKode.OpinionatedFramework.Ensuring](https://www.nuget.org/packages/IOKode.OpinionatedFramework.Generators.Ensuring)
