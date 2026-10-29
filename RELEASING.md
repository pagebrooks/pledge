# Releasing

## Versions

Versions come from git tags through MinVer; nothing in the repository stores a version number.
A tag `vX.Y.Z` or `vX.Y.Z-alpha.N` is the version of that commit. Every other commit builds as
the next patch with an `-alpha.0.<commits-since-tag>` suffix, starting at `0.1.0`.

## Cutting a release

1. Confirm `main` is green.
2. Tag the commit and push the tag:

   ```sh
   git tag v0.1.0
   git push origin v0.1.0
   ```

3. The Release workflow builds, tests and packs, checks the package version against the tag,
   signs the packages if a certificate is configured, pushes them to NuGet.org and creates a
   GitHub release with the packages attached and generated notes. Tags with a pre-release
   suffix are marked as pre-releases.

## One-time setup

- **NuGet.org trusted publishing.** The workflow authenticates with GitHub's OIDC token through
  `NuGet/login`, so no long-lived API key exists. On nuget.org, add a trusted publishing policy
  for repository `pagebrooks/pledge`, workflow `release.yml` and environment `release`, then
  set the repository variable `NUGET_USER` to the nuget.org account that owns the policy.
- **Package signing (optional).** Add the secrets `NUGET_SIGNING_CERTIFICATE` (a base64-encoded
  PFX of a code-signing certificate) and `NUGET_SIGNING_PASSWORD`. When present, packages are
  Authenticode-signed with a timestamp before publishing; when absent, the step is skipped.
- **Environment `release`.** GitHub creates it on the first run. Add required reviewers there
  if publishing should wait for approval.

## Strong naming

Every assembly is strong-named with `Pledge.snk` at the repository root. The key is checked in
on purpose: strong naming gives an assembly an identity for binding and `InternalsVisibleTo`,
not trust, and a public key lets anyone build a binary-compatible Pledge. Trust comes from
package signing and from the reproducible, SourceLinked build. Public key token:
`64d74533f2bd81a4`.
