<!-- ce-bootstrap:SDK-E08 -->

## SDK-E08 — Generation, artifacts, and ecosystem conformance

**Owner:** `CheatEngineNet/CheatEngine.SDK` · **Kind:** epic · **Priority:** P1 · **Milestone:** SDK-M6

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This grouping coordinates the linked implementation issues. Existing capabilities are credited by each child rather than redeclared as missing.

**Evidence classification:** Proposal derived from the attached summaries and pinned source references.

### Outcome and rationale

Make published artifacts and generated consumers match the qualified source contracts.

**Expected benefit:** Make published artifacts and generated consumers match the qualified source contracts.

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

- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/50
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/51
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/52

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

- [S06 — Prior audit: generation stamps but name-based unregister.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/source-generators/CheatEngine.SDK.SourceGenerators.Shared/LuaEmit/LuaRegistrationEmitter.cs)
- [S07 — Prior audit: generated Try calls collapse failure causes.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/libs/CheatEngine.SDK.Lua/CompilerServices/LuaCallSupport.cs)
- [S12 — Standalone publication is not live CE DLL loading.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/tests/CheatEngine.SDK.AotProbe/README.md)
- [N03 — Source/binary/behavioral compatibility distinctions.](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes)
- [C19 — Prior audit: SDK 1.0.0 resolution; artifact bytes not independently inspected.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/packages.lock.json)
- [S14 — Existing release flow; no automatic tags or publications.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/RELEASING.md)
- [H03 — Closed publication task; do not recreate or reopen.](https://github.com/CheatEngineNet/CheatEngine.SDK/issues/13)
- [H01 — Merged foundations; author-reported tests are not independently executed.](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/19)
- [S13 — Live fixture entry point, identified in contribution guidance.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/tests/CheatEngine.SDK.LivePlugin/README.md)
- [S01 — Existing build/test, focused branches and release rules; preserve rather than overwrite.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/CONTRIBUTING.md)
- [C21 — Live gate protocol identified by current README; not independently re-read here.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/docs/live-capability-gates.md)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
