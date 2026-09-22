## Summary

<!-- What changes and why. The squash-merge commit subject is the pull request title: keep it short and imperative. -->

## Scope and ownership

<!-- Affected contracts (ABI, Lua stack, generators, analyzers, package layout, CI) and what is out of scope.
The SDK owns Cheat Engine integration; CheatEngine.Client owns workflows and policy. -->

## Validation

| Check                  | Evidence (command, run link or artifact)                     | Result       |
|------------------------|--------------------------------------------------------------|--------------|
| CI / Gate              | <!-- link to the Pull request CI run -->                     | Pending      |
| Local build and tests  | <!-- e.g. dotnet test --solution CheatEngine.SDK.slnx -c Debug --fail-skips on --> | Not executed |
| Packed package         | <!-- nuget-package artifact of the run, or local dotnet pack --> | Not executed |
| Live Cheat Engine host | <!-- CE build and architecture, or "not applicable" -->      | Not executed |

## Compatibility and release impact

<!-- Public API or behavior changes, ownership, lifetime, cleanup, cancellation and migration. -->

## Checklist

- [ ] The change is focused and follows CONTRIBUTING.md.
- [ ] Tests cover the change; no skipped test hides a failure.
- [ ] Consumer-visible changes are recorded under `[Unreleased]` in CHANGELOG.md.
- [ ] Affected READMEs and documentation are updated in this pull request.
- [ ] The claims above match the actual CI and local results; live-host limitations are stated.
