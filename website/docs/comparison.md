---
title: Compared with other tools
description: How Pledge compares with Metalama, PostSharp, Code Contracts and nullable reference types, including what each one costs.
---

# Compared with other tools

Several .NET tools check conditions on methods. Most of them check values on the way in: is this argument
null, is this number in range. Pledge is built for the half of Design by Contract they leave out:
postconditions that relate a method's result and the object's state to the arguments and to the state before
the call.

This page reflects each tool as of October 30, 2026. If something here is out of date,
[open an issue](https://github.com/pagebrooks/pledge/issues).

## What sets Pledge apart

- **Postconditions are expressions.** `Pledge.Ensures(Balance == Pledge.Old(Balance) - amount)` states what
  the method does, not just what shape its result has. Attribute-based contracts check a value against a fixed
  predicate such as `[NotNull]` or `[Range]`. They cannot relate a result to the arguments or to an earlier
  state.
- **`Old()` snapshots.** A postcondition can compare against any value as it was when the method was entered.
  No maintained .NET tool offers this; Code Contracts did.
- **Contracts become property-based tests.** [`Pledge.Testing`](testing.md) generates inputs, lets the
  preconditions discard the ones they reject, and uses the postconditions as the oracle. You write the
  contract once, and it is checked when the code runs and again by the tests.
- **No rewriter, no compiler fork.** A source generator and C# interceptors run in the standard compiler.
  Nothing rewrites the assembly after it is built and nothing replaces `csc`. The generated code is plain C#,
  so trimming and Native AOT work, and CI publishes a sample with Native AOT to keep it that way.
- **Enforcement modes with C++26's vocabulary.** `Ignore`, `Observe`, `Enforce` and `EnforceFailFast`, set per
  build and per kind of contract. `Observe` reports a violation without throwing, so contracts can be added to
  code that is already running. See [enforcement modes](modes.md).
- **Nothing left when off.** In `Ignore` the assembly is byte-identical to one built without Pledge, and CI
  checks that on every push.
- **Blame.** A broken precondition is reported as the caller's bug and a broken postcondition as the method's,
  with the caller's file and line in the message.

## Capabilities

| | Pledge | Metalama and PostSharp contracts | Code Contracts | Nullable reference types and BCL helpers |
| --- | --- | --- | --- | --- |
| Preconditions | Any boolean expression | Attributes from a fixed vocabulary (`[NotNull]`, `[Range]`, `[Regex]`…) or custom attribute classes | Any boolean expression | Null checks: compile-time warnings, plus `ThrowIfNull` and similar helpers |
| Postconditions | Any boolean expression over the result, the arguments and the object's state | Attributes on return values and `out` parameters | Any boolean expression | Annotations such as `[return: NotNull]`, checked at compile time only |
| `Old()` snapshots | Yes | No | Yes (`Contract.OldValue`) | No |
| Postconditions when the method throws | Yes (`EnsuresOnThrow`) | No | Yes (`EnsuresOnThrow`) | No |
| Object invariants | Planned for v1.0 | Yes (`[Invariant]` methods) | Yes | No |
| Contract inheritance | Planned for v1.0 | Yes: an interface's contracts apply to its implementations | Yes | Annotations are checked on overrides |
| Contracts as property-based tests | Yes | No | No | No |
| How it works | Source generator and interceptors in the standard compiler | Metalama: aspects applied by its own build of the Roslyn compiler. PostSharp: rewrites the IL after compilation | Rewrites the IL after compilation (`ccrewrite`), plus a separate static checker | The compiler and the base class library |
| Setup | One package | The package, which brings its compiler or weaver | Installer and Visual Studio extension | Built in |
| Status | Alpha | Maintained | Archived in 2018; never supported .NET Core | Part of .NET |
| Licence | MIT | See [licensing](#licensing) | MIT | MIT |

Metalama is PostSharp's successor, and both write contracts as attributes. They share a column because their
contracts can say the same kinds of thing; they differ in how they get into your build and in their licences.

A few more tools are related but do a different job:

- **Null-check weavers** such as NullGuard.Fody add null checks to compiled code. Nullable reference types now
  cover most of that at compile time.
- **Guard clause libraries** such as Ardalis.GuardClauses and CommunityToolkit.Diagnostics make hand-written
  argument checks shorter. They cover preconditions only, as code inside the method body.
- **JetBrains `[ContractAnnotation]`** describes input-to-output rules such as `"null => halt"` for ReSharper
  and Rider to check while you edit. Nothing is checked when the program runs.
- **[Dafny](https://dafny.org)** is a language that proves its contracts before compiling to C#, among other
  targets. It gives stronger guarantees, but you write the code in Dafny rather than C#.

## Licensing

Most of these tools are free, including for commercial use. Where money comes in, it is for what surrounds the
free part.

| Tool | Licence | What costs money |
| --- | --- | --- |
| Pledge | MIT | Nothing. There is no paid tier, and none is planned for the core. You can [sponsor its development](https://github.com/sponsors/pagebrooks). |
| [Metalama](https://www.postsharp.net/metalama/pricing) | MIT for the open-source packages, including `Metalama.Patterns.Contracts`. Its pricing page notes that only the latest release line stays under MIT. | The Visual Studio tooling is free in the Community edition for individuals, companies of up to three users and non-commercial use. Larger commercial teams need the paid Professional or Enterprise edition, which also adds toolkits. |
| [PostSharp](https://www.postsharp.net/il/essentials) | Commercial licence agreement | Contracts are free without limit in the free Essentials edition. Most other premium features are limited to 1,000 lines of code without a paid licence. |
| Code Contracts | MIT | Nothing, but it only ever ran on .NET Framework and is no longer maintained. |
| JetBrains.Annotations | MIT | The checks run in ReSharper or Rider, which are free for non-commercial use and paid for commercial use. |
| Dafny | MIT | Nothing. |

The licences are the ones each package's latest release declares on nuget.org. The pricing details come
from the vendors' own pages on October 30, 2026; check those pages before you buy anything.

## Where Pledge falls short

- **Checks happen at call sites the compiler can see.** Pledge checks a call where it is compiled, so calls
  from other assemblies, through delegates or reflection, or through a base class or interface to an override,
  are not checked. [PLEDGE006](/rules/PLEDGE006) points out the ones in your own project. Tools that rewrite IL
  check inside the method instead, so every caller is covered.
- **No invariants or contract inheritance yet.** Both are planned for v1.0. Metalama, PostSharp and Code
  Contracts have them.
- **No attribute vocabulary.** There is no `[Range]` or `[Regex]` library; you write the condition as an
  expression.
- **.NET 10 and C# 14 or later only.** Interceptors need a current compiler. The `Pledge.Attributes` package
  targets `netstandard2.0`, but it only carries metadata.
- **Some checks cost a little.** Simple contracts cost nothing measurable; one that calls a method, or reads
  `this` in a `Requires`, costs about 1.8 ns a call. The generator also misses its incremental build target.
  See [performance](performance.md).
- **It is alpha.** The API can still change between pre-releases.

## Using them together

Pledge does not try to replace the tools around it:

- **Nullable reference types** handle nullness at compile time. Use them, and use Pledge for the rest.
- **Validation libraries** such as FluentValidation and DataAnnotations check input from users and other
  systems. Contracts check your code. [Validation or contracts?](validation-vs-contracts.md) explains where
  each one goes.
- **Guard clauses** still fit the public entry points of a library that other assemblies call, because Pledge
  does not see those calls, and anywhere a check must run in every build.
