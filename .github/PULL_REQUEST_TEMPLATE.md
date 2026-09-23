## Summary

<!-- What changes and why. The squash-merge commit subject is the pull request title: a short imperative sentence of at
most 72 characters, without a prefix or a trailing period (the PR policy check enforces it). -->

## Scope and ownership

<!-- Affected contracts (ABI, Lua stack, generators, analyzers, package layout, CI) and what is out of scope.
The SDK owns Cheat Engine integration; CheatEngine.Client owns workflows and policy. -->

## Validation

| Check                                                                                                  | Evidence (command, run link or artifact)                                                   | Result       |
|--------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------|--------------|
| CI / Gate                                                                                              | <!-- link to the Pull request CI run -->                                                   | Pending      |
| Local build and tests                                                                                  | <!-- e.g. dotnet test --solution CheatEngine.SDK.slnx -c Debug --fail-skips on -->         | Not executed |
| Packed package                                                                                         | <!-- nuget-package artifact of the run, or local dotnet pack -->                           | Not executed |
| Public API files / CompatibilitySuppressions                                                           | <!-- PublicAPI.Unshipped.txt lines and declared breaks, or "no public API change" -->      | Not executed |
| Qualification level (C0 static, C1 managed, C2 native fixture, C3 CE exact, C4 multi-plugin) and Q-IDs | <!-- levels reached and the Q-IDs affected, from the audit's Q01-Q48 register -->           | Not executed |
| Live Cheat Engine host                                                                                 | <!-- describe the host evidence gathered, or "not applicable" -->                          | Not executed |
| Release manifest impact                                                                                | <!-- package content, SBOM or bridge hash changes, or "none" -->                            | Not executed |

<!-- Results use the qualification vocabulary: Passed, Failed, Not executed, or Not applicable with a reason. A C3 or
C4 result stays "Not executed" without evidence gathered against the exact Cheat Engine host. -->

## Compatibility and release impact

<!-- Public API or behavior changes, ownership, lifetime, cleanup, cancellation and migration. -->

## Checklist

- [ ] The change is focused and follows CONTRIBUTING.md.
- [ ] Tests cover the change; no skipped test hides a failure.
- [ ] Consumer-visible changes are recorded under `[Unreleased]`, or the description contains `<!-- changelog: not-needed -->` with a reason
- [ ] Public API changes are recorded in `PublicAPI.Unshipped.txt`; every intentional break is declared in
  `CompatibilitySuppressions.xml` and the CHANGELOG.
- [ ] Lock files were regenerated with `dotnet restore <project> --force-evaluate`, never edited by hand.
- [ ] Evidence tests carry `[Trait("Qualification", "Qxx")]` naming the qualification level reached.
- [ ] Affected READMEs and documentation are updated in this pull request.
- [ ] The claims above match the actual CI and local results; live-host limitations are stated.
