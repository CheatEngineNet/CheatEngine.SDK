# Releasing CheatEngine.SDK

NuGet releases are produced by `.github/workflows/release.yml` from version tags. This document describes the chain,
how to prepare and publish a release, and how anyone can verify one afterwards.

> **Current state.** The last published version is 1.0.0, released before this chain existed: its GitHub release carries
> the nupkg only (no SBOM or checksums) and is not immutable. The 2.0 line has not been released. There is no automated
> qualification gate: before tagging a stable release, the maintainer confirms against the audit's Q02-Q10, Q40 and Q41
> register (analyses/20 of the audit dossier) that the release is not claiming host qualification it has not earned, and
> records any open row as a known limitation in the release notes.

## Overview

```text
verify ─► ci ─► attest ─► draft-release ─► publish ─► verify-publication ─► finalize-release
          │       │             │              │               │                     │
          │       │             │              │               │                     └ Release published and verified
          │       │             │              │               └ nuget.org serves the attested package, repository-signed
          │       │             │              └ push to nuget.org after approval of the nuget environment
          │       │             └ draft release that already carries every asset
          │       └ SBOM and its attestation, provenance attestation, SHA256SUMS
          └ build and test the tag in Debug and Release, pack once, test that exact nupkg
```

| Job                  | What it does                                                                                                                                                                                                                            |
|----------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `verify`             | Checks the SemVer tag, that it points to the first-parent history of `main`, that the version is not on nuget.org yet, and extracts the release notes from `CHANGELOG.md`.                                                              |
| `ci`                 | Runs `ci.yml` on the tag. The Release leg packs `CheatEngine.SDK.<version>.nupkg` (the file name must match the tag), runs the packaging tests on that exact file (`CESDK_PACKAGED_UMBRELLA_NUPKG`), and uploads it as `nuget-package`. |
| `attest`             | Extracts the SPDX 2.2 SBOM embedded in the nupkg, creates the SLSA provenance attestation and the SBOM attestation of the nupkg (predicate `https://spdx.dev/Document/v2.2`), verifies both, and writes `SHA256SUMS`.                   |
| `draft-release`      | Creates a **draft** release that already carries every asset, or completes an existing draft. It never uploads to a published release.                                                                                                  |
| `publish`            | Waits for approval of the `nuget` environment, checks the nupkg against `SHA256SUMS`, logs in through NuGet trusted publishing and pushes.                                                                                              |
| `verify-publication` | Waits until nuget.org lists the version, downloads the repository-signed copy and checks it: `dotnet nuget verify --all`, and every zip entry byte-identical to the attested package except the added `.signature.p7s`.                 |
| `finalize-release`   | Publishes the release, then verifies what a consumer downloads (`SHA256SUMS`, `gh release verify`, `gh attestation verify`).                                                                                                            |

The `nuget-package` artifact is the release artifact: the file the packaging tests consumed is the file that is
attested, attached to the release and pushed to nuget.org. Never upload a locally built package: nuget.org versions
are immutable.

**Why draft-first.** Once an
[immutable release](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/immutable-releases)
is published, its tag cannot move and its assets cannot be added, changed or deleted, so every asset must be attached
to the draft. That is why the attestations are created before publication: the draft must carry their bundles.
`finalize-release` only flips the draft to published once nuget.org has served the attested package.

## Package identities

One release has three package identities: the attested asset, its NuGet content hash, and the nuget.org signed copy.
`verify-publication` checks all three exist and agree; none of them is written to a separate manifest. The published
1.0.0 is the worked example (verified on 2026-09-23):

| Identity               | What it is                                                                                                                                                                                                 | 1.0.0                                                                                                                                                                          |
|------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Attested asset SHA-256 | SHA-256 of the unsigned nupkg CI packed: the GitHub release asset and the subject of the attestations.                                                                                                     | `99bf90101cd13e0183c94759e43badc6a1e719ffc3e2c9fd0b93490abdac0632`                                                                                                             |
| NuGet content hash     | SHA-512 (base64) of that same unsigned file. It is the `contentHash` consumer lock files store and validate on restore ([NU1403](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1403)). | `n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==`                                                                                     |
| nuget.org signed file  | nuget.org [repository-signs every package](https://learn.microsoft.com/nuget/reference/signed-packages-reference), so the file it serves has different bytes: the same entries plus `.signature.p7s`.      | SHA-256 `3e8c98583ac71af25a5bd7053e7583fbafcd196139fae7c0b04bae9b40a7cd33`, SHA-512 `1a2B/E6reX5e636hfdb+Zdj3kT6817DuNES1RWvprhRyuyztE/56Zk2iHOMQIKpGH+O2Va8rYJxXXXTVq5aN9Q==` |

The 1.0.0 signed copy has 28 entries and the asset 27; every common entry is byte-identical, and
`dotnet nuget verify --all` on the signed copy reports the content hash above. The native bridge inside 1.0.0
(`build/native/cheatengine-sdk-lua-bridge.dll`) has SHA-256
`da08c2ba03019da3a8c432ef061d5d6133fd2169ba3a6a8e9ac903353856d994` and source fingerprint
`8a63e00c7dd941212e7ef8c13d8c97f73142c5154bfbe5dbc5459e7131bb789b:2871368515be4c6fd235e49e793d5557e7c50229fcc8fbfd903efd39f9b754a8`.

## One-time setup

### Trusted publishing

Create an organization-owned trusted publishing policy under the `CheatEngine` organization at
<https://www.nuget.org/account/trustedpublishing> with these exact values:

| Field            | Value                                  |
|------------------|----------------------------------------|
| Policy owner     | `CheatEngine` (organization)           |
| Repository owner | `CheatEngineNet`                       |
| Repository       | `CheatEngine.SDK`                      |
| Workflow file    | `release.yml`                          |
| Environment      | `nuget`                                |
| Scope            | Push new packages and package versions |
| Package glob     | `CheatEngine.SDK`                      |

The GitHub `nuget` environment must contain an environment secret named `NUGET_USER`. Its value must be the exact
nuget.org username of the administrator who created the policy, currently `AriusII`, not the organization name and not
an email address. Organization membership alone does not make another username valid for that policy; if a different
administrator recreates it, update `NUGET_USER` to that policy creator. The workflow exchanges GitHub's OIDC token for a
short-lived NuGet API key through `NuGet/login`
([trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)); it never stores a long-lived
NuGet API key. Because the policy names `release.yml` and `nuget`, the login and push steps must stay in the `publish`
job of that file, and the login runs right before the push because the temporary key lives one hour.

### Repository settings

The maintainer applies these settings directly in the repository's GitHub settings:

- Environment `nuget`: the maintainers who approve publications as required reviewers, with self-review allowed
  (`prevent_self_review: false`) while a single maintainer releases; no administrator bypass; deployments limited to
  tags matching `v*.*.*`; the `NUGET_USER` secret.
- A tag ruleset on `v*`: release tags cannot be moved or deleted.
- Immutable releases: enable them **only after this `release.yml` is on `main`**. The previous workflow uploaded onto an
  existing published release, which an immutable release refuses. Until they are enabled, `finalize-release` warns that
  the release has no release attestation and skips `gh release verify`.

As observed on 2026-09-23, the `nuget` environment had a branch policy only (no required reviewer) and immutable
releases were disabled.

## Prepare a release

1. **Changelog.** Move the completed entries from `[Unreleased]` to a `## [X.Y.Z] - YYYY-MM-DD` section of
   `CHANGELOG.md` and add its link reference. The body of that section becomes the GitHub release notes: a stable tag
   fails without it; a prerelease tag uses its own `## [X.Y.Z-rc.N]` section when present, otherwise `[Unreleased]`.
   Keep the four kinds of change the audit distinguishes apart, because each one breaks consumers differently:
    - `### Added`: extensions (a new wrapper, option or API);
    - `### Changed`: semantic corrections (the meaning of a result or boolean, a Lua arity or index convention, the
      allowed thread, ownership conditions, an error category);
    - `### Security`: refusal hardening (an operation that used to proceed and is now refused);
    - `### Deployment`: package, native bridge, runtime policy or load-profile changes.

   `### Removed` and `### Deprecated` are used as Keep a Changelog defines them. Add a `### Qualification waivers`
   section when the [qualification gate](#qualification-gate) needs one.
2. **Version line.** The 2.0 line is set by `MinVerMinimumMajorMinor` in `Directory.Build.props` (`2.0`); the exact
   version comes from the `v<major>.<minor>.<patch>` tag.
3. **API compatibility.** Every pack validates `lib/net10.0` against the published 1.0.0 package, and
   `src/CheatEngine.SDK/CompatibilitySuppressions.xml` lists exactly the intentional breaks; nothing else may differ.
   Every suppressed break has a `### Changed`, `### Removed` or `### Security` entry in the version's section.
4. **Local validation** from the repository root, with .NET SDK 10.0.401:

   ```powershell
   dotnet restore CheatEngine.SDK.slnx --locked-mode
   dotnet build CheatEngine.SDK.slnx -c Debug --no-restore
   dotnet test --solution CheatEngine.SDK.slnx -c Debug --no-build --fail-skips on --filter-not-trait "Category=Packaging"
   dotnet build-server shutdown
   dotnet build CheatEngine.SDK.slnx -c Release --no-restore
   dotnet pack src/CheatEngine.SDK -c Release -o artifacts/nuget -p:MinVerVersionOverride=2.0.0 --no-restore
   $env:CESDK_PACKAGED_UMBRELLA_NUPKG = (Resolve-Path artifacts/nuget/CheatEngine.SDK.2.0.0.nupkg).Path
   dotnet test --solution CheatEngine.SDK.slnx -c Release --no-build --fail-skips on
   Remove-Item Env:CESDK_PACKAGED_UMBRELLA_NUPKG
   ```

   Replace `2.0.0` with the version you rehearse; the pack needs nuget.org once for package validation. The Release test
   run consumes exactly the packed file, as CI's Release leg does
   ([exact-package run](tests/CheatEngine.SDK.Tests/README.md#run-the-tests)). This local package is a rehearsal: never
   upload or publish it.
5. **Merge** the release pull request (squash) once `CI / Gate` passes.

## Qualification

A stable release follows Checkpoint F of the audit: a user must know what they can load, with which version and which
limits. There is no automated gate for this: before tagging a stable release, the maintainer checks the audit's Q02-Q10,
Q40 and Q41 register (analyses/20 of the audit dossier) against what this tree actually qualifies, and adds a
`### Qualification waivers` section to the version's CHANGELOG entry for any row that stays open, as `- Qxx: <reason>`.
The waivers are part of the release notes, so every consumer sees them.

C1 and C2 results are never presented as host qualification: a C1 cell never stands in for a required C3 level.

## Dry run

Start `Release` manually from a branch (**Actions → Release → Run workflow**, or
`gh workflow run release.yml --ref <branch>`). The dry run executes `verify` (a notice instead of the tag checks), the
full `ci`, and `attest` without attestations. It uploads `nuget-package` and `attestation-bundles` (SBOM and
`SHA256SUMS`), creates no draft and publishes nothing.

## Publish

The release commit must first land on the first-parent history of `main`; the workflow rejects a tag from another
commit. From an up-to-date `main` checkout:

```powershell
git tag -a v2.0.0 -m "Release 2.0.0"
git push origin v2.0.0
```

The tag starts the chain of the [overview](#overview). When `draft-release` finishes, the draft release already shows
the nupkg, the SBOM, both attestation bundles and `SHA256SUMS`. `publish` then waits for a required reviewer to
approve the `nuget` deployment on the run page. After the push, nuget.org validates and indexes the package, which can
take several minutes; `verify-publication` polls for up to 35 minutes. `finalize-release` publishes the GitHub release
only after nuget.org serves the attested package, and its step summary shows the immutability status and the asset
checksums.

## Verify a release

Anyone can check a release without trusting this repository's word:

```powershell
$v = '2.0.0'
gh release download "v$v" -R CheatEngineNet/CheatEngine.SDK -D release
Set-Location release

# Immutable release: the release attestation covers the tag, the commit and every asset.
gh release verify "v$v" -R CheatEngineNet/CheatEngine.SDK
gh release verify-asset "v$v" "CheatEngine.SDK.$v.nupkg" -R CheatEngineNet/CheatEngine.SDK

# Build provenance and SBOM of the package, signed by this workflow for this tag.
$identity = @('-R', 'CheatEngineNet/CheatEngine.SDK',
  '--signer-workflow', 'CheatEngineNet/CheatEngine.SDK/.github/workflows/release.yml',
  '--source-ref', "refs/tags/v$v", '--deny-self-hosted-runners')
gh attestation verify "CheatEngine.SDK.$v.nupkg" @identity
gh attestation verify "CheatEngine.SDK.$v.nupkg" @identity --predicate-type https://spdx.dev/Document/v2.2

# Checksums (sha256sum -c SHA256SUMS on Linux and macOS).
Get-Content SHA256SUMS | ForEach-Object {
  $hash, $name = $_ -split '  ', 2
  if ((Get-FileHash $name -Algorithm SHA256).Hash -ne $hash) { throw "$name does not match SHA256SUMS." }
}
```

- The attestation bundles attached to the release (`*.provenance.sigstore.json`, `*.sbom.sigstore.json`) let
  `gh attestation verify` check an asset against a bundle file with `--bundle <file>` instead of fetching the
  attestation from GitHub ([`gh attestation verify`](https://cli.github.com/manual/gh_attestation_verify),
  [verifying a release](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/verifying-the-integrity-of-a-release)).
- A consumer lock file holds the NuGet content hash: the `contentHash` of `CheatEngine.SDK` in `packages.lock.json`
  equals the base64 SHA-512 of the release asset
  (`[Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes("CheatEngine.SDK.$v.nupkg")))`).
- `dotnet nuget verify --all` on the file downloaded from nuget.org reports a nuget.org repository signature and the
  same
  content hash ([`dotnet nuget verify`](https://learn.microsoft.com/dotnet/core/tools/dotnet-nuget-verify)). Never run
  it
  on the GitHub asset: that file is unsigned by design and fails with
  [NU3004](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu3004).
- The `verify-publication` job automates these nuget.org checks directly in the workflow.

**Reproducibility.** The promise is at the level of the DLLs and the native bridge: MinVer stamps the tag version,
`ContinuousIntegrationBuild` normalizes paths, and the source fingerprint embedded in the bridge names its C source and
build script. The nupkg itself is not byte-reproducible, because the SBOM it embeds has a generated document namespace
and creation time. The attested nupkg is therefore the identity of a release; a rebuild from the tag reproduces its
assemblies and bridge, not the nupkg bytes. A packed bridge that differs from the committed, audited one
(`native/cheatengine-sdk-lua-bridge/bridge-audit-manifest.json`) is reported as drift, never hidden and never a
failure: CI rebuilds the bridge with its pinned toolset.

## After a release

On `main`, in one pull request after the release:

1. Raise `MinVerMinimumMajorMinor` in `Directory.Build.props` to the next development line (after `v2.0.0`: `2.1`), so
   untagged builds become `2.1.0-alpha.0.N`.
2. Set `PackageValidationBaselineVersion` in `src/CheatEngine.SDK/CheatEngine.SDK.csproj` to the released version.
3. Delete `src/CheatEngine.SDK/CompatibilitySuppressions.xml`: the new baseline already contains those changes.
4. For each shipping library, apply the `*REMOVED*` lines of `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`, move
   the other lines into `PublicAPI.Shipped.txt` (ordinally sorted), and leave `PublicAPI.Unshipped.txt` with its
   `#nullable enable` header only.
5. Open a new empty `[Unreleased]` section in `CHANGELOG.md`.

## Re-running a release

Use **Re-run failed jobs** only. Completed jobs are not repeated and the re-run reuses the artifacts of the original
attempt, so the tested, attested, attached and pushed package stays the same file. Artifacts are kept 90 days: re-run
within that window.

- A draft that misses assets receives them.
- A push of an existing version is skipped as a duplicate.
- A release that is already published is never uploaded to: when it carries the same assets, `draft-release` reports it
  and succeeds, and `finalize-release` only verifies it.
- **Re-run all jobs** rebuilds a new nupkg (its SBOM differs), so `draft-release` refuses a draft that holds the earlier
  one. If that draft was never published, delete it and re-run. Once the version is on nuget.org, a full re-run stops
  in `verify`.
- If `verify-publication` times out while nuget.org is still validating, re-run it. If it reports a different package,
  stop: do not publish the draft, and investigate the push.
