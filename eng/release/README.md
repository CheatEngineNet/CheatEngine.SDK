# Release tooling

PowerShell 7 scripts that turn the package CI tested into verifiable release evidence: the SBOM it embeds, the
checksums of every release asset, and the release tuple that ties the attested package to its source, build, native
bridge, Cheat Engine profile and qualification evidence. `.github/workflows/release.yml` runs them; the C# tests in
`tests/CheatEngine.SDK.Tests/Release` and `tests/CheatEngine.SDK.Tests/Packaging/ReleaseTupleTests.cs` run them on
synthetic inputs and on the package under test.

## Scripts

| File | What it does | Runs in `release.yml` |
|------|--------------|-----------------------|
| `ReleaseTools.psm1` | Pure functions shared by the scripts and the workflow: hashes, zip entries, nuspec identity, bridge fingerprint, `SHA256SUMS` text, JSON writing, the signed-copy comparison, the draft-release asset plan, the pull request lookup and the qualification gate. No environment, network or step-summary access. | every job below |
| `Export-PackageSbom.ps1` | Extracts `_manifest/spdx_2.2/manifest.spdx.json` from the nupkg byte for byte. Fails when it is absent, is not `SPDX-2.2`, or disagrees with the `.sha256` file the SBOM tool writes next to it. | `attest` |
| `New-Sha256Sums.ps1` | Writes `SHA256SUMS`: `<sha256>  <name>` per asset, ordinal order, LF, final newline, UTF-8 without BOM, the format `sha256sum -c` reads. | `attest` |
| `New-ReleaseTuple.ps1` | Writes `CheatEngine.SDK.<version>.tuple.json` (schema [`release-tuple.v0.schema.json`](release-tuple.v0.schema.json)), `PrePublish` or `Published`. | `attest` (`PrePublish`), `finalize-release` (`Published`) |
| `Test-PublishedPackage.ps1` | Polls the nuget.org flat container (resolved from the service index) until the version is listed, downloads the repository-signed file, runs `dotnet nuget verify --all` on it, checks that it reports the content hash of the attested package and a nuget.org repository signature, and that the signed copy is the attested package plus `.signature.p7s`, every other entry byte-identical. Writes the signed SHA-256 and SHA-512. | `verify-publication` |
| `Test-ReleaseQualification.ps1` | The Checkpoint F gate: rows Q02-Q10, Q40 and Q41 of `docs/qualification/matrix.json` must pass at every required level for the released tree (a C3/C4 pass names that tree or carries a transfer justification), or be listed under `### Qualification waivers` in the release notes. `Enforce` fails on an open row, `Report` lists them in a notice. | `verify` (`Enforce` for a stable tag, `Report` for a prerelease or a dry run) |
| `release-tuple.v0.schema.json` | JSON Schema (draft 2020-12) of the tuple. `ReleaseTupleSchemaTests` keeps it equal to the C# validator the tests use. | — |

Every script fails with a single `::error::` line, which is also an annotation on the workflow run, and writes no
absolute local path into any file it produces. `Test-PublishedPackage.ps1` never runs `dotnet nuget verify` on the
unsigned CI package: an unsigned package has no signature to verify
([NU3004](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu3004)).

## How the jobs hand the assets over

| Job | Reads | Produces |
|-----|-------|----------|
| `verify` | the tag, `CHANGELOG.md`, `docs/qualification/matrix.json` | `release-notes`; the qualification gate verdict |
| `ci` (`ci.yml`) | the tag | `nuget-package` (the one nupkg the Release leg packed and tested), `build-info` |
| `attest` | `nuget-package`, `build-info`, the checkout | the SBOM, the two attestation bundles (tag runs only), `SHA256SUMS` and the `PrePublish` tuple, uploaded as `attestation-bundles` (never a second copy of the nupkg) |
| `draft-release` | `nuget-package`, `attestation-bundles`, `release-notes` | the draft release with every asset; on a re-run, only the missing assets (only the tuple may be replaced) |
| `publish` | `nuget-package`, `attestation-bundles` | the push of the nupkg whose SHA-256 `SHA256SUMS` lists |
| `verify-publication` | `nuget-package`, nuget.org | the nuget.org repository-signed SHA-256 and SHA-512 (job outputs) |
| `finalize-release` | `nuget-package`, `attestation-bundles`, `build-info` | the `Published` tuple and its attestation bundle on the draft, then the published release, verified as downloaded |

## Release assets

| Asset | Content |
|-------|---------|
| `CheatEngine.SDK.<version>.nupkg` | The unsigned package CI packed, tested with `CESDK_PACKAGED_UMBRELLA_NUPKG`, attested and pushed to nuget.org. |
| `CheatEngine.SDK.<version>.spdx.json` | The SPDX 2.2 SBOM embedded in that package, extracted byte for byte. |
| `CheatEngine.SDK.<version>.provenance.sigstore.json` | Sigstore bundle of the SLSA provenance attestation of the nupkg. |
| `CheatEngine.SDK.<version>.sbom.sigstore.json` | Sigstore bundle of the SBOM attestation of the nupkg (predicate `https://spdx.dev/Document/v2.2`). |
| `SHA256SUMS` | SHA-256 of the four files above. |
| `CheatEngine.SDK.<version>.tuple.json` | The release tuple: `PrePublish` on the draft, replaced by the `Published` tuple before the release is published. Its `assets` mirror `SHA256SUMS`. |
| `CheatEngine.SDK.<version>.tuple.sigstore.json` | Sigstore bundle of the provenance attestation of the `Published` tuple. |

A dry run (`workflow_dispatch`) produces the SBOM, `SHA256SUMS` and a `PrePublish` tuple without attestations, and no
release.

## The release tuple

The tuple is built from files, never from workflow inputs:

- the nupkg: SHA-256, SHA-512 (the NuGet `contentHash` of the unsigned package, the value consumer lock files store),
  nuspec id, version and repository commit, the packed bridge `build/native/cheatengine-sdk-lua-bridge.dll` (SHA-256
  and the source fingerprint found in its bytes, without loading it) and the embedded SBOM;
- `build-info.json` of the CI run that packed it: commit, tree, run URL, .NET SDK, runner image, native toolchain, and
  the package and bridge it describes. A build-info that names another package, another bridge SHA-256 or another
  fingerprint fails the script; a nuspec commit other than the build-info commit fails it too;
- `native/cheatengine-sdk-lua-bridge/bridge-audit-manifest.json`: when the packed bridge bytes differ from the
  committed, audited bridge, the script emits a notice and a step-summary line, never a failure (CI rebuilds the bridge
  with its pinned toolset);
- `docs/qualification/support-profile.json` (the `Qualifiable` profile id and the file hash), `matrix.json` (its hash)
  and every committed receipt `docs/qualification/receipts/*/R-*.json` (id, scenario, level, status, hash; event logs
  are not listed). When a file is absent the tuple records `null` and the workflow shows a warning; a scenario without
  a committed receipt is not listed, so no host result is ever invented;
- `SHA256SUMS` of the release assets, each hash checked against its file.

Hashes of committed JSON documents are taken after CRLF to LF normalization, so they equal the git blob hash input on
every checkout. A `Published` tuple also requires the nuget.org repository-signed SHA-256 and SHA-512, the verified
repository signature, both attestation bundles and the tag.

## Reproducibility

The promise is at the level of the DLLs and the native bridge: MinVer stamps the tag version, `ContinuousIntegrationBuild`
normalizes paths, and the bridge is built twice and compared by the `native` job. The nupkg itself is not
byte-reproducible, because the SBOM it embeds carries a generated document namespace and a creation time. The attested
nupkg and its hashes in the tuple are therefore the identity of a release, not a rebuild of it.
