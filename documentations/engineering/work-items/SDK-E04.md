<!-- ce-bootstrap:SDK-E04 -->

## SDK-E04 — Runtime identity and resource ownership

**Owner:** `CheatEngineNet/CheatEngine.SDK` · **Kind:** epic · **Priority:** P1 · **Milestone:** SDK-M2

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This grouping coordinates the linked implementation issues. Existing capabilities are credited by each child rather than redeclared as missing.

**Evidence classification:** Proposal derived from the attached summaries and pinned source references.

### Outcome and rationale

Make runtime/target authority and exception-safe cleanup reusable for every SDK consumer.

**Expected benefit:** Make runtime/target authority and exception-safe cleanup reusable for every SDK consumer.

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

- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/38
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/39
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/40

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

- [S04 — Prior audit: current-target allocation bindings.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/libs/CheatEngine.SDK.Engine/Allocation/LuaTargetMemoryAllocationOperations.cs)
- [S05 — Prior audit: patch and disable-info ownership handoff.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/libs/CheatEngine.SDK.Engine/Assembly/AutoAssemblerPatcher.cs)
- [C08 — Prior audit: local observed epoch is not authoritative target identity.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Infrastructure/TargetSelectionLifetime.cs)
- [C06 — Prior audit: expected no-target failure and local target observations.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/ProcessClient.cs)
- [S03 — Prior audit: new factory already exists; qualify, do not recreate.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/libs/CheatEngine.SDK.Engine/Scanning/Values/MemoryScanSessions.cs)
- [C12 — Prior audit: Parent/destroy semantics and mutation/snapshot ambiguity.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/SdkTableRecordMutationPort.cs)
- [S11 — Protected native boundary and bridge contract reference.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/native/cheatengine-sdk-lua-bridge/README.md)
- [U04 — Historical per-thread coroutine/shared Lua VM model.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaHandler.pas)
- [N02 — A longjmp cannot cross or skip managed frames.](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/exceptions-interoperability)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
