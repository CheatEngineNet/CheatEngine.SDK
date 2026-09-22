# CheatEngine.SDK documentation

> Recreated 2026-09 from the audit, not the historical documentations/ tree.

This folder holds what the CheatEngineNet audit of 2026-09-22 (`MANIFESTE.md` SHA-256
`7179b0691d27cba0589b3f5fa00945ddbb7c04b362500df3de30726550f4ff6e`) requires to be distributable with the SDK (ADR-12):
the restrictions, hashes, source references and qualification results that a reader needs to check a compatibility
statement from this repository alone, without access to anyone's private workspace. Every page is rebuilt from the
audit; none is a restored copy of an earlier document.

## Contents

| Area                                                                                       | Page                                                                                 | Status          |
|--------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|-----------------|
| Qualification: support profiles, Q01–Q48 matrix, receipts of executed runs, local protocol | [qualification/README.md](qualification/README.md)                                   | Placeholder     |
| ABI and NativeAOT restrictions                                                             | [abi/README.md](abi/README.md), [abi/nativeaot-profile.md](abi/nativeaot-profile.md) | Placeholder     |
| Lua surface catalogue, deferred families, four coverage measures                           | [catalog/README.md](catalog/README.md)                                               | Placeholder     |
| Audit traceability                                                                         | `audit-2026-09-22-traceability.md`, added at the end of the remediation              | Not written yet |

A placeholder page states its scope and the work that replaces it; it records no evidence yet.

## Evidence levels

C0 static contract, C1 managed tests, C2 native fixture, C3 exact Cheat Engine host with a loaded plugin, C4
multi-component (two plugins, target switch). A C1 or C2 result is never presented as host-qualified. Details:
[qualification/README.md](qualification/README.md).

## Retired documentation

Commit `4020a32` removed the `documentations/` tree and `native/cheatengine-sdk-lua-bridge/AUDIT.md` on 2026-09-22. They
are **not restored**. Statements that relied on them are **declared, not recovered** (evidence kind `DeclaredRepo`) until
a page listed here re-establishes them with its own evidence. Their removal does not mean that the work they described
never existed; it means that a reader of this repository cannot check it.

| Old path                                                                                    | Replacement                                                                                                                                               | Note                                                                    |
|---------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| `documentations/CheatEngine.SDK/capability-matrix.md`                                       | [catalog/README.md](catalog/README.md) for per-surface provenance and semantics; [qualification/README.md](qualification/README.md) for executed evidence | The two concerns are separate pages.                                    |
| `documentations/CheatEngine.SDK/SOURCES.md`                                                 | [qualification/support-profile.md](qualification/support-profile.md)                                                                                      | Cheat Engine, Lua module and `celua.txt` hashes.                        |
| `documentations/CheatEngine.SDK/advanced-domains/`                                          | [catalog/README.md](catalog/README.md)                                                                                                                    | Deferred families.                                                      |
| `documentations/CheatEngine.SDK/live-probes/README.md`                                      | [qualification/local-protocol.md](qualification/local-protocol.md)                                                                                        | Recording and redaction rules of exact-host runs.                       |
| `documentations/CheatEngine.SDK/catalog/*.json`                                             | [catalog/README.md](catalog/README.md)                                                                                                                    | Rebuilt from scratch, not restored.                                     |
| `documentations/engineering/ADR-006-nativeaot-plugin-loader-profile.md`                     | [abi/nativeaot-profile.md](abi/nativeaot-profile.md)                                                                                                      | F02, Q41 and Q42 restrictions.                                          |
| `documentations/engineering/work-items/SDK-*.md`, `documentations/engineering/backlog.json` | None                                                                                                                                                      | [ROADMAP](../ROADMAP.md) keeps the `SDK-0xx` identifiers as plain text. |
| `documentations/engineering/ARCHIVE_RECONCILIATION.md`                                      | [Retired identifiers](#retired-identifiers)                                                                                                               | Architecture-review scenario identifiers.                               |
| `native/cheatengine-sdk-lua-bridge/AUDIT.md`                                                | [Bridge README](../native/cheatengine-sdk-lua-bridge/README.md)                                                                                           | Contract, build and delivery of the Lua protection bridge.              |

## Retired identifiers

Some READMEs cited architecture-review scenarios such as `R25/T049`. These identifiers come from an archive that is not
in this repository; the retired `ARCHIVE_RECONCILIATION.md` was the only page that listed them. They are replaced by the
qualification scenarios of the audit:

| Retired identifiers | Scope, as the Hosting and Coexistence READMEs described it                                                     | Audit scenario |
|---------------------|----------------------------------------------------------------------------------------------------------------|----------------|
| R25/T049–T050       | Two plugins in one Cheat Engine process: shared or separate SDK assemblies, independent activation and removal | Q09, Q10       |
| R26/T051–T052       | First-thread Lua acquisition, refusal, and cross-plugin worker/main-thread concurrency                         | Q19            |
| R34/T067–T068/T076  | Target switch and retained allocation/patch ownership                                                          | Q30            |

The mapping follows the scope that the Hosting and Coexistence READMEs describe for each identifier. It names the
closest audit scenario; it is not a transcription of the unrecovered archive.
