# GitHub Deployment Receipt

## Purpose and evidence boundary

This is a read receipt for the authorized GitHub import reviewed on September 21, 2026. It records planning metadata only. It does not claim a source build, a package artifact, a fixture pass, a live Cheat Engine run, or a release decision.

The historical 403 responses described in [EVIDENCE_AND_LIMITS.md](EVIDENCE_AND_LIMITS.md) belong to an earlier preparation session. They neither invalidate nor replace the later live GitHub deployment described here.

## Read-back scope

| Object | Observed receipt | Interpretation |
|---|---|---|
| SDK roadmap root | [Issue #20](https://github.com/CheatEngineNet/CheatEngine.SDK/issues/20), marker `ce-bootstrap:SDK-PLAN` | One navigation root; it is not a GitHub Project object or a delivered feature. |
| SDK hierarchy | Issues #21–#28 are the eight epics; #29–#52 are the 24 leaves | The root/epic/leaf structure is native issue hierarchy, not inferred from labels alone. |
| SDK dependency graph | Read back from the issue dependency API and compared with `backlog.json` | Internal `blocked_by` edges and external `blocks` edges are planning relationships, not implementation completion. |
| Client references | CLI-007/#23, CLI-008/#24, CLI-009/#25, CLI-010/#26, CLI-012/#28, CLI-013/#29, CLI-014/#30, CLI-015/#31, CLI-016/#32, CLI-017/#33, CLI-018/#34, CLI-022/#38, CLI-024/#40 | These are explicit downstream adoption references. They do not move Client work into SDK. |
| Organization board | [CheatEngineNet Project #1](https://github.com/orgs/CheatEngineNet/projects/1), 66 items at read-back | Membership and configured fields are live state, separate from this versioned manifest. |
| Bootstrap review | [SDK PR #53](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/53), branch `chore/engineering-bootstrap-2026-09-21` | This PR is the reviewable documentation/tooling import and must remain one focused commit. |

## Reproducible operator checks

Use an independently authenticated GitHub CLI session with read access. The following are read-only checks and intentionally do not create, close, relabel, or rearrange GitHub objects.

```powershell
gh issue view 20 --repo CheatEngineNet/CheatEngine.SDK
gh pr view 53 --repo CheatEngineNet/CheatEngine.SDK
gh project item-list 1 --owner CheatEngineNet --format json --limit 500
python eng/Validate-EngineeringManifest.py
python -m unittest discover -s eng/tests -v
```

The local Python commands validate only the checked-in graph and its mirror files. Native GitHub relationships, board fields/views, and all product gates require the separate live read-back described in [VALIDATION_PROTOCOL.md](VALIDATION_PROTOCOL.md).
