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
   signs the packages if a certificate is configured, attests their provenance, pushes them to
   NuGet.org and creates a GitHub release with the packages attached and generated notes. Tags
   with a pre-release suffix are marked as pre-releases.

## Dry run

Run the Release workflow by hand from the Actions tab on any branch. It builds, tests, packs and
signs exactly as a tagged run does and uploads the packages as a workflow artifact, but it does
not attest, publish or create a release. Use it to exercise the pipeline before the first tag and
after any change to it.

## Provenance

Every tagged run publishes a [build provenance attestation](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations)
for each package: a signed statement that those exact bytes were produced by this repository's
release workflow at that commit. Verify a file downloaded from the GitHub release with:

```sh
gh attestation verify Pledge.0.1.0.nupkg --repo pagebrooks/pledge
```

This works on the files attached to the GitHub release. A package downloaded from nuget.org will
not verify, because nuget.org adds its own repository signature to every package and that changes
the bytes; for those, the author signature (when configured) and nuget.org's signature are the
checks. The attestation is taken after signing, so it covers the signed package.

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
- **Pinned actions.** Workflow actions are pinned to commit SHAs with the version in a comment.
  Dependabot updates both; do not replace a SHA with a floating tag.

## Strong naming

Every assembly is strong-named with `Pledge.snk` at the repository root. The key is checked in
on purpose: strong naming gives an assembly an identity for binding and `InternalsVisibleTo`,
not trust, and a public key lets anyone build a binary-compatible Pledge. Trust comes from
package signing and from the reproducible, SourceLinked build. Public key token:
`64d74533f2bd81a4`.
