---
title: Performance
description: What contract checks and the generator cost, measured with BenchmarkDotNet against the performance budget.
---

# Performance

Pledge has a performance budget, and the benchmarks in
[`benchmarks/`](https://github.com/pagebrooks/pledge/tree/main/benchmarks) measure it. This page shows
the latest results, including the targets that are not met yet.

## Checks at run time

Each contracted method is measured against the same check written by hand, the way a guard clause is
written today. The contracts always pass: a broken contract is a bug, not a hot path.

| Scenario | Hand-written | Pledge | Allocated (both) | Budget |
| --- | ---: | ---: | ---: | --- |
| Precondition only | 0.0 ns | 0.0 ns | 0 B | No more than the hand-written check; no allocation |
| Postcondition with one `Old` of a value type | 0.0 ns | 0.0 ns | 0 B | One local; no allocation |
| Async method (`Task<int>`, completes synchronously) | 58.2 ns | 57.3 ns | 144 B | One extra state machine, measured against a hand-written wrapper |

Neither path allocates, and the async wrapper allocates no more than a hand-written one; its time is within
the few nanoseconds these runs vary by. Both sync checks compile down to nothing beyond the comparison itself:
the JIT inlines Pledge's checked wrapper into the caller, just as it inlines a hand-written guard, so neither
shows up in the measurement.

That holds for contracts whose expression cannot throw: comparisons and `&&`/`||` on parameters, locals,
literals, `Result()` and `Old()` values, null and type checks, and fields or auto-properties of `this` in an
`Ensures`. A contract that can throw, for example by calling a method, reading an ordinary property, or
reading `this` in a `Requires`, is evaluated inside a `try/catch`. That way an exception from the expression
is reported as a violation of that contract, with the exception as its `Cause`. The JIT does not inline
the wrapper around such a check, which costs about 1.8 ns a call on this machine.

## The generator at build time

A project of 1,000 contracted methods in 50 files, each method called once, under `Enforce`:

| Scenario | Time | Allocated | Budget |
| --- | ---: | ---: | --- |
| First build | 627 ms | 136 MB | |
| After editing one contract | 477 ms | 62 MB | 50 ms |

This misses its target by a wide margin. Roslyn runs the generator's semantic analysis again for every
contracted method in the project after any edit, and reading one method's contracts costs about half a
millisecond, about 25 µs of it deciding which checks can skip the `try/catch`. A cheaper contract reader is
open work. Each edit already regenerates only the wrappers whose method or call sites changed.

## Reproduce

```bash
dotnet run -c Release --project benchmarks/Pledge.Benchmarks -- --filter '*'
dotnet run -c Release --project benchmarks/Pledge.Roslyn.Benchmarks -- --filter '*'
```

These results come from BenchmarkDotNet 0.15.8 on .NET 10.0.10 (SDK 10.0.302), on a 12th Gen Intel Core
i7-1255U running Debian 12, measured Oct 30, 2026. Absolute numbers vary by machine; the comparison with the
hand-written baseline is the part that carries over.
