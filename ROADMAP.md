# CheatEngine.SDK Roadmap

**Planning baseline: September 21, 2026.** This is an outcome-based plan, not a delivery-date commitment. The initial preparation session was denied GitHub writes; this branch is the later reviewable import. The roadmap describes planned outcomes, never implementation, package, fixture, or live-host completion. Read the live issue and Project state separately from this versioned plan.

## Operating boundary

SDK owns CE integration; Client owns developer-facing workflows. Independent reliability fixes need not wait for all SDK research. Source merged, package shipped, fixture passed and host qualified are separate gates.

## Milestones

| Phase | Outcome | Exit evidence |
|---|---|---|
| SDK-M0 | Evidence and governance baseline | Archive-continuity gaps recorded; reviewed governance; source/artifact/support claims distinguished. |
| SDK-M1 | ABI and host safety qualification | Layout/signature and lifecycle evidence attached; native AOT loading remains a separate decision. |
| SDK-M2 | Authoritative integration contracts | Factual outcomes, target identity and ownership contracts have deterministic tests and no Client dependency. |
| SDK-M3 | Memory, scan, and minimum artifacts | Per-primitive gates recorded and minimum containing SDK artifact identified for Client consumption. |
| SDK-M4 | Assembly and debugger contracts | Instruction, patch and continuation responsibilities qualified or explicitly deferred. |
| SDK-M5 | Advanced capability boundaries | Each optional family has independent prerequisites; no generic enable-all capability. |
| SDK-M6 | Release and ecosystem conformance | Declared release scope has package hashes, tested profiles and reproducible evidence. |

## Epics and implementable work

### SDK-E01 — Evidence, governance, and capability provenance
Maintain the source-indexed execution baseline without inventing audit coverage.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-001](documentations/engineering/work-items/SDK-001.md) — Establish source, artifact, and capability provenance | P1 | Refinement and evidence; no declared issue blocker |
| [SDK-002](documentations/engineering/work-items/SDK-002.md) — Inventory the public CE extension surface and host profiles | P1 | SDK-001 |
| [SDK-003](documentations/engineering/work-items/SDK-003.md) — Adopt engineering governance and validate the bootstrap graph | P1 | Refinement and evidence; no declared issue blocker |

### SDK-E02 — ABI, hosting, and deployment qualification
Qualify exact host contracts and preserve the supported managed deployment.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-004](documentations/engineering/work-items/SDK-004.md) — Qualify classic ABI layouts and conflicting signatures | P1 | SDK-002 |
| [SDK-005](documentations/engineering/work-items/SDK-005.md) — Qualify activation admission, shutdown, and plugin coexistence | P1 | SDK-002 |
| [SDK-006](documentations/engineering/work-items/SDK-006.md) — Decide the NativeAOT plugin loader profile without weakening managed support | P2 | SDK-005 |

### SDK-E03 — Authoritative semantic outcomes and Lua registration
Expose reusable CE semantics without depending on Client policy.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-007](documentations/engineering/work-items/SDK-007.md) — Preserve structured outcomes across Lua and Engine primitives | P1 | SDK-001 |
| [SDK-008](documentations/engineering/work-items/SDK-008.md) — Own built-in runtime, process, symbol, and table Lua contracts | P1 | SDK-007, SDK-002 |
| [SDK-009](documentations/engineering/work-items/SDK-009.md) — Return ownership-aware Lua registration leases | P1 | SDK-007, SDK-012 |

### SDK-E04 — Runtime identity and resource ownership
Make runtime/target authority and exception-safe cleanup reusable for every SDK consumer.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-010](documentations/engineering/work-items/SDK-010.md) — Establish authoritative target identity for effectful operations | P1 | SDK-007, SDK-002 |
| [SDK-011](documentations/engineering/work-items/SDK-011.md) — Make resource ownership handoff and cleanup exception-safe | P1 | SDK-010, SDK-007 |
| [SDK-012](documentations/engineering/work-items/SDK-012.md) — Qualify shared Lua state, reset, and protected operation boundaries | P1 | SDK-002, SDK-005 |

### SDK-E05 — Memory and scanning primitives
Provide qualified target reads, AOB outcomes, and scan-session ownership.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-013](documentations/engineering/work-items/SDK-013.md) — Qualify target memory, pointer width, and bounded buffer contracts | P1 | SDK-007, SDK-010 |
| [SDK-014](documentations/engineering/work-items/SDK-014.md) — Separate AOB absence, errors, and execution bounds | P1 | SDK-007, SDK-013 |
| [SDK-015](documentations/engineering/work-items/SDK-015.md) — Qualify the existing value-scan session factory | P2 | SDK-011, SDK-013 |

### SDK-E06 — Instruction, patch, and debugger contracts
Separate instruction processing, target mutation and immediate callback decisions.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-016](documentations/engineering/work-items/SDK-016.md) — Qualify assembly and disassembly contracts by instruction profile | P2 | SDK-004, SDK-013 |
| [SDK-017](documentations/engineering/work-items/SDK-017.md) — Qualify Auto Assembler patch application and disable ownership | P1 | SDK-011, SDK-012 |
| [SDK-018](documentations/engineering/work-items/SDK-018.md) — Define synchronous debugger callback and continuation ownership | P2 | SDK-004, SDK-005, SDK-010 |

### SDK-E07 — Record commands and optional capability families
Deliver exact record semantics early and keep unrelated advanced families independently gated.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-019](documentations/engineering/work-items/SDK-019.md) — Qualify timer and hotkey subscription ownership | P2 | SDK-005, SDK-012 |
| [SDK-020](documentations/engineering/work-items/SDK-020.md) — Partition advanced capability research into independently gated families | P3 | SDK-002, SDK-010 |
| [SDK-021](documentations/engineering/work-items/SDK-021.md) — Expose typed record and symbol mutation ownership | P1 | SDK-007, SDK-012 |

### SDK-E08 — Generation, artifacts, and ecosystem conformance
Make published artifacts and generated consumers match the qualified source contracts.

| Work item | Priority | Prerequisites |
|---|---|---|
| [SDK-022](documentations/engineering/work-items/SDK-022.md) — Validate generated bindings and marshalling in packed consumers | P1 | SDK-007, SDK-009 |
| [SDK-023](documentations/engineering/work-items/SDK-023.md) — Publish a traceable minimum contract artifact for Client adoption | P1 | SDK-008, SDK-009, SDK-010, SDK-011, SDK-021, SDK-022 |
| [SDK-024](documentations/engineering/work-items/SDK-024.md) — Establish release qualification and performance evidence | P2 | SDK-023, SDK-005 |

## Execution notes

A phase is an outcome grouping, not a global lock. A research task can proceed while an unrelated reliability fix ships. The graph specifies technical prerequisites; it does not estimate capacity. Before Client adoption, identify a package containing every required SDK primitive even when a prior SDK minimum-contract release is already complete.

Create a branch only when a leaf is ready. Keep SDK and Client PRs separate; publish the containing SDK artifact before declaring dependent Client behavior supported. Parent issue closure is never inferred from a single child PR.
