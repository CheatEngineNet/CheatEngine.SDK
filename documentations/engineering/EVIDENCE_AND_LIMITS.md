# Evidence, Provenance, and Deployment Limits

## Historical preparation boundary

The following limitation records the **initial preparation session** from which this bootstrap was derived. It is not a claim about a later review workstation or the current GitHub state: the complete ZIPs referenced by the initial summaries were not mounted there. That session did not claim a byte-for-byte import, exhaustive reconciliation of the earlier 40 and 36 findings, or a verified mapping to every original ADR/test ID. SDK-001 and CLI-001 explicitly track that continuity work. New stable planning IDs are used instead of inventing original IDs.

The subsequent architecture-review package used for this PR audit is identified and reconciled in [ARCHIVE_RECONCILIATION.md](ARCHIVE_RECONCILIATION.md). Its validation is documentation/data/tooling evidence only; it does not retroactively turn a proposed finding into product or live-host evidence.

## Current repository observations

| Repository | Main inspected | Relevant merged work |
|---|---|---|
| CheatEngine.SDK | `aa3fcc3cdf629468e69d0c68817183d44a719894` | PR #19: owned runtime primitives and generated Lua marshalling. |
| CheatEngine.Client | `923a4ded85898f53ef4cd2ff5872d2fd9001071a` | PR #7: fluent high-level APIs and generated Lua modules. |

Both repositories reported no open issues/PRs at the initial metadata read; an organization issue search found no open issues in these two repositories. The SDK search returned eight historical issues, including closed NuGet/publication and documentation work. Those were not reopened. Main is protected. The metadata's user-level push/admin flags did not establish the active integration's effective write permissions.

SDK CONTRIBUTING and AGENTS and the Client README were read in this bootstrap. Relevant .github/root metadata was inspected to preserve existing conventions. Most deep implementation references come from the prior review; they are marked `PriorAuditReference`, not misrepresented as newly executed or fully reread. The prior official CE comparison remains pinned to `ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`, not an inspected live CE 7.7 binary.

## Historical write attempts

| Operation | SDK | Client |
|---|---|---|
| Create bootstrap branch | HTTP 403 | HTTP 403 |
| Create roadmap issue | HTTP 403 | HTTP 403 |

Each response reported `Resource not accessible by integration`. No branch, commit, PR, issue, label, milestone, native relationship or organization Project was successfully created **by that initial session**. No `evidence/deployment-attempts.json` is included in this repository; this table is the retained historical receipt. Repeatedly changing endpoints or generating privileged workflows would not repair those permissions and was not attempted.

Later operator-authorized deployment is a distinct evidence event. Its authoritative receipt is the live GitHub state, including [SDK-PLAN](https://github.com/CheatEngineNet/CheatEngine.SDK/issues/20), its child issues, the organization Project, and this bootstrap PR. Do not rewrite the historical table as if it described that later deployment.

## Tool coverage

The connected GitHub actions support several issue/file/branch/PR operations, but do not expose full milestone creation, label-definition creation, Project administration, or native hierarchy/dependency mutation. The supplied GitHub CLI tool uses the documented APIs after the operator authenticates independently with the necessary authorization. It does not obtain, alter, print or transmit credentials outside GitHub. The remaining local CLI path is prepared, not live executed here.

## Verification categories

`ReadThisSession`, `MetadataRead`, `PriorAuditReference`, `WebPrimary`, `Proposal`, `FixtureVerified` and `LiveVerified` are distinct. Planning records carry no FixtureVerified or LiveVerified status. The local validation report covers this package and its Python tests only. No .NET/native build, repository test suite, live CE experiment, package restore or performance benchmark was executed.

The archive is not a source clone and does not contain the missing original audit ZIPs. Existing implementation claims are credited, but qualification work must recheck exact source and actual containing package. Package version, source commit and assembly version remain separate identifiers.
