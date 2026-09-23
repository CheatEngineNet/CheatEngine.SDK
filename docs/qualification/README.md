# Qualification

> Recreated 2026-09 from the audit, not the historical documentations/ tree.

This folder holds the evidence of what CheatEngine.SDK has actually been shown to do: the Cheat Engine host
[support profiles](support-profile.md), the Q01–Q48 [qualification matrix](matrix.json) of the 2026-09-22 audit
(`analyses/20`, identified by the SHA-256 `7179b0691d27cba0589b3f5fa00945ddbb7c04b362500df3de30726550f4ff6e` of its
`MANIFESTE.md`), the v0 [schemas](schemas/) of these documents and, once a host run has happened, the committed
receipts. The 48 scenarios are an acceptance plan: a row changes only with an executed, traited test (C0–C2) or a
committed receipt (C3/C4). There is no global score anywhere.

## Evidence levels

| Level | Environment                        | What it can establish                                                                         |
|-------|------------------------------------|-----------------------------------------------------------------------------------------------|
| C0    | Static contract: sources, analyzers | Theoretical offsets, signatures, references, absence of a forbidden dependency               |
| C1    | Managed tests with doubles and seams | Composition, application rollback, typing, decisions, error mapping                          |
| C2    | Native fixture: controlled Lua and bridge | Lua C API contracts, allocation failures, stack and state restoration                   |
| C3    | The exact Cheat Engine binary with the plugin really loaded | Bootstrap, affinity, Cheat Engine objects, lifecycles, chosen runtime and effective APIs |
| C4    | Several components: two plugins and a controlled target | Coexistence, target switches, interference, side-by-side profiles                    |

A C1 success never counts as a C3 success, and a NativeAOT publish that succeeds is never a Cheat Engine load or unload.
Only C3 and C4 cells can make a profile host-qualified, and only through committed receipts.

## Reading a matrix cell

- **status**: `NotExecuted`, `Passed`, `Failed` or `NotApplicable`. `NotApplicable` always carries a justification; a
  `NotExecuted` cell never carries evidence or a date.
- **passKind** (only when `Passed`): `Functional` when the behaviour works, `RefusalVerified` when the expected outcome is
  a refusal and the test proves the refusal. A refusal is never reported as a functional success.
- **evidenceKind**: `ToQualify` while not executed; `ObservedSource` for a C0–C2 cell backed by tests executed on the
  source tree; `ObservedHost` for a C3/C4 cell backed by a receipt; `ProposedDecision` for a `NotApplicable` that
  follows from a profile decision (see the [Checkpoint A decisions](support-profile.md#checkpoint-a-decisions)).
  Declared values stay `DeclaredRepo` until something measures them.
- **evidence**: for C0–C2, `Automated` entries naming a `*.Tests` project of the solution, the file, `Class.Method`
  and the trait `Qualification=Qxx` the method carries; for C3/C4, `Receipt` entries naming a committed receipt and its
  hash. C1/C2 evidence is never accepted in a C3/C4 cell.
- **profileId**: every C3/C4 cell names the qualifiable profile. The documentary profile `ce-public-src-ec45d5f` can
  never back a `Passed` or `Failed` cell.

Each row also states its scenario: preconditions, operation, expected result and an **expectedCategory** — `Effect`
(the operation takes effect as requested), `Partial` (part of it takes effect and the partial effect is reported),
`Refused` (it is refused or fails cleanly with no effect left behind) or `Unknown` (the effect cannot be established
in advance and is reported as observed). `title` is an English summary; `titleFr` is the audit's wording, verbatim:
for a sub-row, the narrowest fragment the audit states (for example `analyses/12` line 39), otherwise the parent's
scenario.

**Sub-rows** split a scenario into facets (`Q05.a`, `Q30.a`–`Q30.e`, `Q32.a`–`Q32.d`…). At every level where sub-rows
have cells, the parent equals their aggregate: `Failed` if any is `Failed`, `NotApplicable` if all are, `Passed` if all
are `Passed` or justified `NotApplicable`, otherwise `NotExecuted`. A sub-row gets a fixture-level cell when its owner
adds the test that evidences it.

**Traits.** A test that evidences a scenario carries `[Trait("Qualification", "Qxx")]` on the method, and the matrix
cites it; every trait in the test sources is cited and every citation resolves to a traited method of a CI test module.
A trait on a top-level test class applies, as in xUnit, to every `[Fact]`/`[Theory]` method of that class, and the
matrix then cites each of those methods.
`dotnet test --project <tests> --filter-trait "Qualification=Q07"` runs exactly the evidence of one row. The mapping of
the existing tests was checked against the audit's success criterion of each row; tests whose assertions do not prove
the criterion stay untagged.

## Matrix summary

Generated from [`matrix.json`](matrix.json) and checked by
`QualificationMatrixTests.Matrix_summary_in_the_readme_equals_the_matrix`; "–" means the level has no cell.

<!-- BEGIN GENERATED: matrix-summary -->
| Row | Scenario | Owner | Required | C0 | C1 | C2 | C3 | C4 |
|-----|----------|-------|----------|----|----|----|----|----|
| Q01 | PluginVersion record and field offsets | SDK | C0, C1 | Passed | Passed | – | – | – |
| Q02 | Compact bootstrap record between guard bytes | SDK | C1, C3 | – | Passed | – | Not executed | – |
| Q03 | Reduced managed exports table or missing pointer | SDK | C1, C3 | – | Passed (refusal verified) | – | Not executed | – |
| Q04 | Observation of the second bootstrap integer | SDK | C3 | – | Passed | – | Not executed | – |
| Q05 | Name before enable, then enable, disable and enable | SDK | C3 | – | Passed | – | Not executed | – |
| Q05.a | Non-ASCII plugin name | SDK | C3 | – | Passed | – | Not executed | – |
| Q06 | Exception during construction or enable | SDK | C1, C3 | – | Passed | – | Not executed | – |
| Q07 | Re-entrant disable from a running callback | SDK | C1, C3 | – | Passed (refusal verified) | – | Not executed | – |
| Q08 | Partial cleanup, then diagnosis | SDK | C1, C3 | – | Passed | – | Not executed | – |
| Q08.a | Plugin disabled between an allocation and its publication | SDK | C3 | – | – | – | Not executed | – |
| Q09 | Two plugins sharing or not sharing the SDK assemblies | Both | C4 | – | – | – | – | Not executed |
| Q09.a | Coexistence with one shared SDK assemblies folder | Both | C4 | – | – | – | – | Not executed |
| Q09.b | Coexistence with separate plugin folders | Both | C4 | – | – | – | – | Not executed |
| Q10 | Two package versions in separate folders | Both | C4 | – | – | – | – | Not executed |
| Q11 | Missing Lua module or incomplete exports | SDK | C1, C2 | – | Passed (refusal verified) | Passed | – | – |
| Q12 | Allocation failure while growing the Lua stack | SDK | C2 | – | – | Passed | – | – |
| Q13 | Failure of string, table, userdata or reference creation | SDK | C2 | – | – | Not executed | – | – |
| Q14 | Managed callback that throws | SDK | C2, C3 | – | – | Passed | Not executed | – |
| Q15 | Callback kept after disable | SDK | C2, C3 | – | – | Passed | Not executed | – |
| Q16 | Global collision, replacement, then third-party replacement | Both | C2, C4 | – | – | Passed | – | Not executed |
| Q17 | Controlled Lua state replacement | SDK | C2, C3 | – | Passed | Passed | Not executed | – |
| Q18 | Use of a reference after an external reset | SDK | C3 | – | – | – | Not executed | – |
| Q19 | First calls from two workers | SDK | C3, C4 | – | – | – | Not executed | Not executed |
| Q20 | Bytes with NUL and multibyte strings | Both | C1, C2, C3 | – | Not executed | Not executed | Not executed | – |
| Q21 | Signed and unsigned 32-bit values and 64-bit boundaries | Both | C1, C2, C3 | – | Not executed | Not executed | Not executed | – |
| Q22 | nil, false, zero, zero results and Lua error | SDK | C2, C3 | – | – | Not executed | Not executed | – |
| Q23 | CE object, light userdata and foreign userdata | SDK | C2, C3 | – | – | Not executed | Not executed | – |
| Q24 | Bound method and 0-based indexers | SDK | C2, C3 | – | – | Not executed | Not executed | – |
| Q25 | Scanner created, then list creation or publication fails | SDK | C1, C3 | – | Not executed | – | Not executed | – |
| Q26 | FirstScan, NextScan, results and destruction | SDK | C3 | – | – | – | Not executed | – |
| Q27 | AOB scan: empty, error and malformed result | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q28 | AOB scan in a module with matches outside the module | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q29 | Result limit and cancellation during copy | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q30 | Reused PID and target switch | Both | C3, C4 | – | – | – | Not executed | Not executed |
| Q30.a | Targets A and B and a reused PID | Both | C3, C4 | – | – | – | Not executed | Not executed |
| Q30.b | Cleanup after a target switch | Both | C3, C4 | – | – | – | Not executed | Not executed |
| Q30.c | File opened as a process | Both | C1, C3 | – | Not executed | – | Not applicable | – |
| Q30.d | CEServer target | Both | C1, C3 | – | Not executed | – | Not applicable | – |
| Q30.e | Reuse of an old allocation address | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q31 | Overridden Cheat Engine pointer size | Both | C3 | – | – | – | Not executed | – |
| Q31.a | Configured pointer size smaller than the process width | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q32 | x86, x64, ARM or unknown host and target backends | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q32.a | x64 target | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q32.b | x86 target | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q32.c | x86 Cheat Engine host | Both | C3 | – | – | – | Not applicable | – |
| Q32.d | ARM or unknown backend | Both | C1, C3 | – | Not executed | – | Not applicable | – |
| Q33 | Memory batch that fails after several writes | Client | C1, C3 | – | Not applicable | – | Not applicable | – |
| Q34 | Record destroyed or table reloaded | Both | C3 | – | – | – | Not executed | – |
| Q35 | Script activation, then incomplete rollback | SDK | C3 | – | – | – | Not executed | – |
| Q36 | One-shot timer finished before Dispose | SDK | C3 | – | – | – | Not executed | – |
| Q37 | Hotkey callback during module shutdown | SDK | C3 | – | – | – | Not executed | – |
| Q38 | Debug or process-watcher event on a secondary thread | SDK | C3 | – | – | – | Not applicable | – |
| Q39 | Reduced or mutated classic exports table | SDK | C1, C3 | – | Passed (refusal verified) | – | Not applicable | – |
| Q40 | Clean installation from the package | Both | C3 | – | – | – | Not executed | – |
| Q41 | NativeAOT publish and export inspection | SDK | C0, C2 | Not executed | – | Not executed | – | – |
| Q42 | Removal of a NativeAOT plugin profile | SDK | C3, C4 | – | – | – | Not applicable | Not applicable |
| Q43 | Client cleanup with a faulty module | Client | C1, C3 | – | Not applicable | – | Not applicable | – |
| Q44 | A contract-only API is called | Client | C1, C3 | – | Not applicable | – | Not applicable | – |
| Q45 | Sensitive availability probe | Client | C1, C3 | – | Not applicable | – | Not applicable | – |
| Q46 | Logs containing user data or expressions | Both | C1, C3 | – | Not executed | – | Not executed | – |
| Q47 | Inherited property or method and public alias | SDK | C2, C3 | – | – | Not executed | Not executed | – |
| Q48 | SDK package updated without adapting the Client | Both | C1, C3 | Passed | Not executed | – | Not executed | – |
<!-- END GENERATED: matrix-summary -->

The rows still missing evidence are listed in the [support profile](support-profile.md#not-executed).

## Receipts

A C3 or C4 result is a receipt produced by the local runner
[`eng/qualification/Invoke-LocalQualification.ps1`](../../eng/qualification/README.md), never by CI. The runner copies
the profiled Cheat Engine installation into a sandbox, builds the plugins from the exact CI package into a clean folder,
drives Cheat Engine with an autorun Lua driver, restores the operator's registry state, and writes one receipt per
scenario: `receipts/<Qid>/<receiptId>.json` with its structured, redacted event log `<receiptId>.events.json` beside it.
The [local qualification protocol](local-protocol.md) describes the operator's side. A receipt names the tree, the pull
request and head commit, the package identities, the host, bridge, bundle and target hashes, the registry difference
(names only), the preconditions, operation, expected and observed results, the status and the timings.

Receipts are committed together with the matrix cell they qualify. Never committed: Cheat Engine or target binaries,
authorization manifests, registry exports, raw debug output.

## Hash and freshness rules

- A `sha256` that names a committed JSON document (a receipt, an event log, the matrix, the support profile) is the
  SHA-256 of its UTF-8 bytes after CRLF is normalized to LF. Working trees check text out with CRLF
  (`.gitattributes`), while the committed blob and every clone's hash input are LF, so the rule gives the same value
  everywhere.
- A receipt is valid for the tree (`git rev-parse HEAD^{tree}`) and the package it names; with squash merges the tree
  hash, the pull request number and head commit, and the package SHA-256 are the durable identity. A C3/C4 `Passed` or
  `Failed` cell names `treeHash` and `nupkgSha256`; a cited receipt from another tree or package requires the cell's
  `transferJustification`. Without it, the result counts as `NotExecuted` for the new tree or package.

## Schemas and tests

| Document                                | Schema                                                                                     |
|-----------------------------------------|--------------------------------------------------------------------------------------------|
| [`support-profile.json`](support-profile.json) | [`support-profile.v0.schema.json`](schemas/support-profile.v0.schema.json)          |
| [`matrix.json`](matrix.json)            | [`qualification-matrix.v0.schema.json`](schemas/qualification-matrix.v0.schema.json)       |
| `receipts/<Qid>/<receiptId>.json`       | [`qualification-receipt.v0.schema.json`](schemas/qualification-receipt.v0.schema.json)     |
| `receipts/<Qid>/<receiptId>.events.json` | [`qualification-events.v0.schema.json`](schemas/qualification-events.v0.schema.json)      |

The schemas are JSON Schema draft 2020-12 with closed objects. The C# tests in
`tests/CheatEngine.SDK.Repository.Tests/Qualification` validate every document with a validator for exactly the keywords
the schemas use, plus the rules a schema cannot express (`QualificationSchemaTests`, `SupportProfileTests`,
`QualificationMatrixTests`, `QualificationReceiptTests`). The JSON documents are kept in a canonical form (two-space
indentation, LF, readable non-ASCII, rows sorted by id) so that concurrent edits of different rows merge line by line.
