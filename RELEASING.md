# Releasing CheatEngine.SDK

NuGet releases are produced by `.github/workflows/release.yml` from version tags. The workflow verifies, builds, tests,
packs, attests, and publishes the package before creating the matching GitHub release. Do not upload a locally built
package manually: nuget.org versions are immutable, and the workflow artifact is the release artifact.

## One-time trusted publishing setup

Create an organization-owned trusted publishing policy under the `CheatEngine` organization at
<https://www.nuget.org/account/trustedpublishing> with these exact values:

| Field | Value |
|---|---|
| Policy owner | `CheatEngine` (organization) |
| Repository owner | `CheatEngineNet` |
| Repository | `CheatEngine.SDK` |
| Workflow file | `release.yml` |
| Environment | `nuget` |
| Scope | Push new packages and package versions |
| Package glob | `CheatEngine.SDK` |

The GitHub `nuget` environment must contain an environment secret named `NUGET_USER`. Its value must be the exact
nuget.org username of the administrator who created the policy, currently `AriusII`, not the organization name and not
an email address. Organization membership alone does not make another username valid for that policy; if a different
administrator recreates it, update `NUGET_USER` to that policy creator. Restrict the environment to deployment tags
matching `v*.*.*`. The workflow exchanges GitHub's OIDC token for a one-use, short-lived NuGet API key through
`NuGet/login`; it must not store a long-lived NuGet API key.

## Prepare a release

1. Move the completed entries from `Unreleased` to a versioned section in `CHANGELOG.md` and use the release date.
2. Update version-specific examples and analyzer release tracking when the public baseline changes.
3. Set `MinVerMinimumMajorMinor` in `Directory.Build.props` to the release line. The exact version still comes from the
   `v<major>.<minor>.<patch>` tag.
4. Run the local validation from the repository root:

   ```powershell
   dotnet restore CheatEngine.SDK.slnx
   dotnet build CheatEngine.SDK.slnx -c Debug --no-restore
   dotnet test --solution CheatEngine.SDK.slnx -c Debug --fail-skips on
   dotnet test --solution CheatEngine.SDK.slnx -c Release
   dotnet pack src/CheatEngine.SDK -c Release -o artifacts/nuget -p:MinVerVersionOverride=1.0.0 --no-restore
   ```

   Replace `1.0.0` only in the local pack command when rehearsing another release. Inspect the resulting `.nupkg` as a
   ZIP archive and confirm its ID, version, README, license, assemblies, analyzers, build assets, and native bridge.

## Publish

The release commit must first land on the first-parent history of `main`; the workflow rejects a tag from another
commit. From an up-to-date `main` checkout:

```powershell
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
```

The tag starts the `Release` workflow. Confirm that all jobs pass, then verify both the
[NuGet package](https://www.nuget.org/packages/CheatEngine.SDK) and the generated GitHub release. NuGet validation and
search indexing can take several minutes.

After a successful release, raise `MinVerMinimumMajorMinor` to the next development line and commit that change on
`main`. For example, after `v1.0.0`, use `1.1` so subsequent untagged builds become `1.1.0-alpha.0.N`.
