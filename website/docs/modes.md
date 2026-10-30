---
title: Enforcement modes
description: Choose per build and per kind of contract whether contracts are ignored, observed, enforced or fatal.
---

# Enforcement modes

Whether contracts are checked, and what a violation does, is a build setting. Pledge borrows C++26's
vocabulary for it.

| Mode | What happens on a violation | Default in |
| --- | --- | --- |
| `Ignore` | Nothing. No checking code is generated. | Release |
| `Observe` | `PledgeRuntime.ViolationHandler` is called and execution continues. | |
| `Enforce` | The handler is called, then `PreconditionViolationException` or `PostconditionViolationException` is thrown. | Debug |
| `EnforceFailFast` | The handler is called, then the process ends with `Environment.FailFast`. | |

Set it in the project file:

```xml
<PropertyGroup>
  <PledgeMode>Enforce</PledgeMode>
  <!-- Optional: override one kind of contract. EnsuresOnThrow counts as a postcondition. -->
  <PledgePreconditions>Enforce</PledgePreconditions>
  <PledgePostconditions>Observe</PledgePostconditions>
</PropertyGroup>
```

Values are case-insensitive, and an unknown value fails the build rather than being guessed at.

## Ignore means gone

In `Ignore`, the generator emits nothing for the contracts and the `Pledge.*` calls compile away. CI builds
a contracted project in `Ignore` and the same project with the contract lines deleted and without the
package, and checks that the two assemblies are byte-identical.

By default the contracts are still recorded as assembly attributes (`[Requires]`, `[Ensures]`) so tools
can read them. Set `<PledgeMetadata>false</PledgeMetadata>` to leave those out too.

## Observing violations

`Observe` is for rolling contracts into an existing codebase: violations are reported and the code carries
on. Route them to your logging:

```csharp
PledgeRuntime.ViolationHandler = violation =>
    logger.LogWarning("{Kind} of {Member} violated: {Expression}", violation.Kind, violation.Member, violation.Expression);
```

The handler receives a `PledgeViolation` with the kind, the contract's text and message, the member, who is
to blame (the caller for a precondition, the method for a postcondition), the caller's file and line, and
any exception involved. It is also called in `Enforce` and `EnforceFailFast`, before the throw or exit.

## Exceptions

Every exception Pledge throws derives from `BrokenPledgeException`, which carries the `PledgeViolation`:

- `PreconditionViolationException` for `Requires`;
- `PostconditionViolationException` for `Ensures` and `EnsuresOnThrow`. When an `EnsuresOnThrow` contract
  is broken, the exception the method threw is the `InnerException`.

## Property-test registry

The [testing bridge](testing.md) needs a probe for each contracted method. The generator emits them unless
every kind of contract is `Ignore`, so a project you property-test in Release sets:

```xml
<PledgeRegistry>true</PledgeRegistry>
```
