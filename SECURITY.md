# Security

## Reporting a vulnerability

Report privately through [GitHub Security Advisories](../../security/advisories/new). Please do not
open a public issue for anything exploitable.

Expect an acknowledgement within a few days. Pledge is maintained by one person, so a fix is a
matter of severity and available evenings rather than a service-level agreement; you will get an
honest estimate rather than a generous one.

## What Pledge does, and does not, do

- **Everything happens at compile time.** The generator and analyzers run inside the C# compiler
  and emit ordinary C# into your build. There is no runtime code generation, no reflection and no
  dynamic loading.
- **It makes no network requests and collects no telemetry.** Not at build time, not at run time.
  The only thing that talks to the network is this repository's CI.
- **The runtime is small and inert.** It evaluates the checks the generator emitted and throws a
  `BrokenPledgeException` or calls your violation handler. It never swallows an exception: a
  contract that fails while your method is throwing lets the original exception propagate.

## In scope

- Generated code that changes a program's behaviour beyond evaluating the contracts written in
  the source, or that evaluates a contract differently from what the source says.
- A way to make a violated contract pass silently, or an `Ignore`-mode build that is not
  byte-identical to a build without the package.
- The generator or analyzers reading or writing anything outside the compilation they were given.
- Supply-chain issues in the release pipeline. Release packages carry
  [build provenance attestations](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations)
  and can be verified against the files attached to the GitHub release with
  `gh attestation verify <file> --repo pagebrooks/pledge`. Every assembly is strong-named with the
  key in this repository; strong naming is identity, not trust, and the attestation and package
  signature are what to check.

## Out of scope

- Build failures, analyzer diagnostics, or generator crashes on unusual source. These are bugs;
  file an ordinary issue.
- A contract violation at run time. That is the library doing its job: the exception names the
  contract and the member, and the blame is on the caller or the callee, not on Pledge.
- Findings in projects that consume Pledge.

## Supported versions

The most recent release.
