# Architecture Review Package Reconciliation

## Purpose and boundary

This document records how the external **CheatEngineNet Architecture Review — 2026-09-21** package informed the SDK bootstrap review. The archive is review context, not repository source, a shipped package, or live Cheat Engine qualification evidence. It is not copied into this repository.

The reviewed archive had SHA-256 `fc1f178916ec14fb993e405018112ddedd8dc316a86aa6fe9251b42c09547802`. Its internal validator passed with 62 sources, 36 findings, 80 specified-but-not-executed product scenarios, and 32 ownership rows. That validation checks documentation/data/tool integrity only.

The archive baselines are SDK `aa3fcc3cdf629468e69d0c68817183d44a719894` and Client `923a4ded85898f53ef4cd2ff5872d2fd9001071a`. Source HEAD, a published NuGet package, a fixture result, and a live host profile remain distinct evidence objects.

## Reconciliation into the SDK roadmap

| Archive evidence | SDK bootstrap adjustment | Why it belongs here |
|---|---|---|
| Decision brief; R01, R16–R19 | SDK-001, SDK-007, SDK-008, SDK-010, SDK-023 | CE mappings, detailed outcomes, target facts, and the containing package are SDK-owned prerequisites for Client workflows. |
| R05–R07, R20–R21, R27; scenarios T009–T014 and T039–T042 | SDK-010, SDK-011, SDK-012, SDK-017 | Target-mutating owners need provenance, acquisition recovery, explicit release outcomes, and independent invalidation before Client adapters may become available. |
| R10–R15, R22; scenarios T019–T030, T043–T044, T078–T079 | SDK-007, SDK-013, SDK-014, SDK-015 | The SDK owns primitive fidelity and scan/target constraints; Client retains policy, cardinality, budgets, and fluent intent. |
| R08–R09, R30–R32; scenarios T015–T018 and T059–T064 | SDK-009, SDK-018, SDK-022, SDK-023 | Lua registration safety, callback continuations, generated-consumer compilation, and package assets require SDK mechanics and artifact checks. |
| R25–R26, R34; scenarios T049–T052 and T067–T068 | SDK-004, SDK-005, SDK-006, SDK-012, SDK-018 | AOT publication is not CE Native AOT loader evidence; ABI, shared Lua state, lifecycle, and multi-plugin behavior remain profile-specific qualification work. |
| R31–R36; scenarios T061–T076 | SDK-003, SDK-022, SDK-023, SDK-024 | The roadmap keeps generated code, package consumers, behavioral compatibility, performance, and vertical-slice evidence separate from a source-only change. |

## Complete finding and scenario trace

The table below is a trace index, not evidence that a finding has been remediated or a scenario has run. A row that names Client-owned work records the boundary deliberately: this SDK bootstrap supplies prerequisite planning and must not absorb Client policy or workflow implementation.

| Archive identifiers | Roadmap treatment | Qualification boundary |
|---|---|---|
| R01, R16, R17, R18, R19 | SDK-001, SDK-007, SDK-008, SDK-010, SDK-023 | Reconcile CE mappings and qualified integration facts before Client consumption. |
| R02, R03, R04, R23, R24, R28, R29 | Cross-repository boundary; SDK-001 and SDK-023 prerequisites only | Client retains fluent policy, DI composition, packaging adoption, and application-facing contracts. |
| R05, R06, R07, R20, R21, R27 | SDK-010, SDK-011, SDK-012, SDK-017 | Target authority, resource ownership, failure outcomes, and controlled mutation remain SDK mechanics. |
| R08, R09, R30, R31, R32 | SDK-009, SDK-018, SDK-022, SDK-023 | Lua registration, callback lifetime, generated consumers, and artifact checks require SDK evidence. |
| R10, R11, R12, R13, R14, R15, R22 | SDK-007, SDK-013, SDK-014, SDK-015 | Primitive outcomes, memory/pointer width, AOB bounds, and scan ownership precede higher-level policies. |
| R25, R26, R34 | SDK-004, SDK-005, SDK-006, SDK-012, SDK-018 | ABI, host lifecycle, shared Lua state, and Native AOT loader qualification remain distinct profiles. |
| R33, R35, R36 | SDK-003, SDK-022, SDK-023, SDK-024 | Governance, package consumers, compatibility, and release evidence remain independently reviewable. |
| T001, T002, T003, T004, T005, T006, T007, T008 | SDK-001 through SDK-006 | Evidence identity, ABI, lifecycle, coexistence, and loader-profile scenarios. |
| T009, T010, T011, T012, T013, T014, T015, T016 | SDK-007 through SDK-010 | Structured Lua outcomes, registration, dispatch, and target identity scenarios. |
| T017, T018, T019, T020, T021, T022, T023, T024 | SDK-009 through SDK-013 | Lua reset, native ownership, bounded memory, and pointer-width scenarios. |
| T025, T026, T027, T028, T029, T030, T031, T032 | SDK-013 through SDK-015 | Buffer, AOB, value-scan, cancellation, and lifecycle scenarios. |
| T033, T034, T035, T036, T037, T038, T039, T040 | SDK-016 through SDK-018 | Assembly, debugger continuation, Auto Assembler, and controlled-mutation scenarios. |
| T041, T042, T043, T044, T045, T046, T047, T048 | SDK-011, SDK-017 through SDK-021 | Resource cleanup, tables/records, callbacks, timers, and optional capability scenarios. |
| T049, T050, T051, T052, T053, T054, T055, T056 | SDK-004 through SDK-006, SDK-022 | Native AOT, managed plugin deployment, package, and generated-consumer scenarios. |
| T057, T058, T059, T060, T061, T062, T063, T064 | SDK-009, SDK-018, SDK-022, SDK-023 | Lua stack, callbacks, package artifact, and source-generation scenarios. |
| T065, T066, T067, T068, T069, T070, T071, T072 | SDK-023, SDK-024 | Compatibility, performance, package identity, and release-qualification scenarios. |
| T073, T074, T075, T076, T077, T078, T079, T080 | SDK-003, SDK-022 through SDK-024 | Vertical slices, behavioral compatibility, packed consumers, and final release evidence. |

## Consequences for this bootstrap PR

- The work-item order follows the archive's dependency-first migration sequence: evidence and host qualification precede semantic contracts; target/resource authority precedes scans and mutations; a containing artifact precedes Client adoption.
- The bootstrap makes no runtime, package, release, capability-availability, Native AOT plugin, or exact-host claims. The archive's 80 scenarios remain planned until their own evidence is attached.
- Cross-repository edges are declared as stable planning IDs in `backlog.json`; GitHub issue numbers are live deployment data and are deliberately not embedded in the static graph.
- Historical initial-session access limits remain preserved in [EVIDENCE_AND_LIMITS.md](EVIDENCE_AND_LIMITS.md), but they no longer describe the current review context or deployed Project state.

## Review checklist

Before advancing a work item, re-read the archive finding, the mapped acceptance scenarios, the pinned source path, and the actual containing package. Do not infer a live-host pass from this reconciliation or from the archive's successful documentation validation.
