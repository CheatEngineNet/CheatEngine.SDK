# Repository settings

The GitHub settings this repository depends on, as reviewed JSON payloads, and
[`Set-RepositorySettings.ps1`](Set-RepositorySettings.ps1), which applies them idempotently. A repository administrator
runs it from a terminal; CI never does (the script refuses to run when `CI` or `GITHUB_ACTIONS` is set).

Audit anchor: register row PR-CQ-17 (idempotent settings script, applied by the maintainer after an explicit OK).

## What it manages

| Payload | GitHub endpoint | Effect |
|---|---|---|
| [`repository.json`](repository.json) | `PATCH /repos/{owner}/{repo}` | squash merges only, titled by the pull request (the subject on `main` is the title the `PR policy` check verified), branches deleted on merge, issues and Discussions on, secret scanning and push protection on |
| (script) | `PUT .../vulnerability-alerts`, `.../automated-security-fixes`, `.../private-vulnerability-reporting` | Dependabot alerts, Dependabot security updates and private vulnerability reporting stay on |
| [`labels.json`](labels.json) | `POST` or `PATCH .../labels` | every label the issue forms, Dependabot and the scheduled health issue apply exists (GitHub drops unknown labels silently) |
| [`rulesets/protect-main.json`](rulesets/protect-main.json) | `PUT` or `POST .../rulesets` | the default branch: no deletion or force push, pull requests merged by squash, and the two required checks `CI / Gate` and `PR policy`, both pinned to the GitHub Actions app (`integration_id` 15368) |
| [`rulesets/protect-release-tags.json`](rulesets/protect-release-tags.json) | `PUT` or `POST .../rulesets` | `v*` tags start `release.yml`: only repository administrators create, move or delete them |
| [`environments/nuget.json`](environments/nuget.json) | `PUT .../environments/nuget`, `POST .../deployment-branch-policies` | the publish job waits for a reviewer (`-NuGetReviewer`), administrators cannot bypass it, only `v*.*.*` tags deploy |
| [`actions-permissions.json`](actions-permissions.json) | `PUT .../actions/permissions` | Actions on, every action pinned to a full commit SHA (`sha_pinning_required`) |
| [`actions-workflow-permissions.json`](actions-workflow-permissions.json) | `PUT .../actions/permissions/workflow` | the default `GITHUB_TOKEN` is read-only and cannot approve pull requests |
| (script, `-EnableImmutableReleases`) | `PUT .../immutable-releases` | published releases and their tags cannot change |

Each payload carries a `_comment` array that explains its choices; the script never sends it. The payloads name only
the fields they manage: the script compares each payload with the live value as a subset, so a field GitHub adds later
is left alone (`-WhatIf` lists these unmanaged fields for rulesets and the environment). An array of objects (rules,
required checks, reviewers, bypass actors) is compared element by element, and an element that exists only on GitHub
is reported, because a `PUT` replaces the whole array.

What the script never changes: code scanning default setup (it only warns when it is on, because GitHub rejects the
advanced CodeQL uploads of [`codeql.yml`](../../.github/workflows/codeql.yml) while it is), and automatic dependency
submission, which needs an organization-level permission; the
[`dependency-submission.yml`](../../.github/workflows/dependency-submission.yml) workflow submits the NuGet graph instead.

## Prerequisites

- PowerShell 7 and the GitHub CLI, signed in (`gh auth login`) with an account that is an administrator of the
  repository. The script checks both before it reads anything else.
- For the required checks: `main` is green and the `PR policy` check has reported at least once on a pull request.
  Requiring a check that never reported blocks every pull request; until then, pass `-SkipRequiredChecks`, which leaves
  the required checks of `Protect main` as they are.
- For `-EnableImmutableReleases`: the draft-first `release.yml` is on `main`. An immutable release cannot receive an
  asset after publication, which the previous upload fallback relied on.
- `sha_pinning_required` makes GitHub refuse any workflow run whose `uses:` references (including those inside
  composite actions such as `zizmorcore/zizmor-action`) are not full commit SHAs. The repository tests check this for
  the workflows and the local composite action; check the third-party composite actions before applying it.

## Run order

```powershell
./eng/github/Set-RepositorySettings.ps1 -PlanOnly                        # every step and body; no network access
./eng/github/Set-RepositorySettings.ps1 -SkipRequiredChecks -WhatIf     # live comparison; writes nothing
./eng/github/Set-RepositorySettings.ps1 -SkipRequiredChecks             # applies, one confirmation per write
./eng/github/Set-RepositorySettings.ps1 -WhatIf                          # later: with the required checks
./eng/github/Set-RepositorySettings.ps1 -NuGetReviewer AriusII -Confirm:$false
```

A run ends by comparing every step again. A second run changes nothing. If GitHub keeps
"Allow administrators to bypass configured protection rules" on for the `nuget` environment (the documented
environment API does not list `can_admins_bypass`), the script says so and the setting is changed under
Settings > Environments > nuget.

## Tests

`RepositorySettingsPayloadTests` and `RepositorySettingsScriptTests` in
[`tests/CheatEngine.SDK.Repository.Tests`](../../tests/CheatEngine.SDK.Repository.Tests/README.md) check the payloads
(two required checks from GitHub Actions matching the workflow job names, squash only, admin-only release tags, the
environment, SHA pinning, labels) and run the script offline against a recording `gh`: `-PlanOnly` makes no call, CI is
refused, a run against the settings read on 2026-09-23 writes exactly the differences, a run against applied settings
writes nothing, `-WhatIf` never writes, and `-SkipRequiredChecks` never removes checks that are already required.
