<!-- ce-bootstrap:SDK-E02 -->

## SDK-E02 — ABI, hosting, and deployment qualification

**Owner:** `CheatEngineNet/CheatEngine.SDK` · **Kind:** epic · **Priority:** P1 · **Milestone:** SDK-M1

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This grouping coordinates the linked implementation issues. Existing capabilities are credited by each child rather than redeclared as missing.

**Evidence classification:** Proposal derived from the attached summaries and pinned source references.

### Outcome and rationale

Qualify exact host contracts and preserve the supported managed deployment.

**Expected benefit:** Qualify exact host contracts and preserve the supported managed deployment.

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

- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/32
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/33
- https://github.com/CheatEngineNet/CheatEngine.SDK/issues/34

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

- [S10 — ABI scope reference, not certification of every table slot.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/libs/CheatEngine.SDK.Abi/README.md)
- [U01 — Historical loader/tables/callback dispatch comparison.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin.pas)
- [U02 — Historical Pascal implementations versus C declarations.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/pluginexports.pas)
- [U03 — Classic declarations; pointer slots do not all denote direct functions.](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin/cepluginsdk.h)
- [S08 — Prior audit: static host state and runtime admission.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/libs/CheatEngine.SDK.Hosting/Bootstrap/PluginHost.cs)
- [S13 — Live fixture entry point, identified in contribution guidance.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/tests/CheatEngine.SDK.LivePlugin/README.md)
- [C07 — Prior audit: early construction cleanup differs from normal cleanup.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Hosting/CheatEngineClientPlugin.cs)
- [S12 — Standalone publication is not live CE DLL loading.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/tests/CheatEngine.SDK.AotProbe/README.md)
- [N01 — NativeAOT libraries are not unloadable with FreeLibrary.](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
