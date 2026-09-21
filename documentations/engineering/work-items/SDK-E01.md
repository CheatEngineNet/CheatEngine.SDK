<!-- ce-bootstrap:SDK-E01 -->

## SDK-E01 — Evidence, governance, and capability provenance

**Owner:** `CheatEngineNet/CheatEngine.SDK` · **Kind:** epic · **Priority:** P1 · **Milestone:** SDK-M0

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This grouping coordinates the linked implementation issues. Existing capabilities are credited by each child rather than redeclared as missing.

**Evidence classification:** Proposal derived from the attached summaries and pinned source references.

### Outcome and rationale

Maintain the source-indexed execution baseline without inventing audit coverage.

**Expected benefit:** Maintain the source-indexed execution baseline without inventing audit coverage.

### Architectural responsibility

SDK owns CE mappings, native safety, factual outcomes and low-level owners. Client owns application policy, typed workflows, composition, and developer experience.

### Technical requirements and task checklist

- [ ] Review child source evidence and preserve the SDK/Client responsibility boundary.
- [ ] Sequence only real dependencies; do not block a child on closing its own parent.
- [ ] Require an explicit decision and evidence for any deferred capability.

### Acceptance criteria

- [ ] Each child is completed with evidence or explicitly deferred by an approved decision.
- [ ] Cross-repository artifact gates have named versions and hashes when adopted.
- [ ] Compatibility and support documentation match actual delivery, not interface count.

### Required validation

- [ ] Review the acceptance evidence of each child; do not count parent closure as an additional runtime test.

### Scope exclusions

- No monolithic epic implementation PR.

### Parent and children

Parent: https://github.com/CheatEngineNet/CheatEngine.SDK/issues/20

- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/29
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/30
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/31

### Blocked by

None declared. This does not waive evidence, policy, or package requirements.

### Blocks

None declared. This does not waive evidence, policy, or package requirements.

### Package and readiness gate

Identify the first containing artifact before describing new source as consumable support. No version is invented by this plan.

### Proposed branch and PR

No epic-sized implementation branch. Children have focused branch/PR proposals. The bootstrap creates only one documentation/tooling branch and draft PR per repository.

### Risks and compatibility

A grouping can span several phases. Its completion milestone is not a reason to delay an earlier independent child.

**Long-term impact:** One traceable owner, stable acceptance criteria, and explicit compatibility evidence reduce repeated integration fixes and make future API changes reviewable.

### Definition of done

- [ ] Linked child PRs, tests, and a final scope/deferral review.

Outcome group; optional scope remains explicitly gated or deferred.

### Sources

- [S01 — Existing build/test, focused branches and release rules; preserve rather than overwrite.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/CONTRIBUTING.md)
- [H01 — Merged foundations; author-reported tests are not independently executed.](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/19)
- [C19 — Prior audit: SDK 1.0.0 resolution; artifact bytes not independently inspected.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/packages.lock.json)
- [U01 — Historical loader/tables/callback dispatch comparison.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin.pas)
- [U02 — Historical Pascal implementations versus C declarations.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/pluginexports.pas)
- [U03 — Classic declarations; pointer slots do not all denote direct functions.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin/cepluginsdk.h)
- [U04 — Historical per-thread coroutine/shared Lua VM model.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaHandler.pas)
- [S10 — ABI scope reference, not certification of every table slot.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/libs/CheatEngine.SDK.Abi/README.md)
- [S02 — No LINQ; imperative commits; no Co-authored-by trailer; preserve native identifiers.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/AGENTS.md)
- [G01 — Issue API; filter pull_request entries from issue lists.](https://docs.github.com/en/rest/issues/issues)
- [G02 — Repository-specific milestones.](https://docs.github.com/en/rest/issues/milestones)
- [G03 — Native hierarchy uses issue IDs, not issue numbers.](https://docs.github.com/en/rest/issues/sub-issues)
- [G04 — Native blocked-by relationship with the blocker issue_id.](https://docs.github.com/en/rest/issues/issue-dependencies)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
