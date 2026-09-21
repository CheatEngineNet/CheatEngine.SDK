# Engineering Operating Model

## Authority and source of truth

The versioned roadmap records intent, architecture, acceptance evidence and proposed sequencing. GitHub issues record current execution, decisions and delivery links. Pull requests contain reviewable changes. The organization Project aggregates those same issues; it is not a second copy of their specifications. Repository milestones are outcome groups. Epic issues are parents, not monolithic implementation tasks.

The bootstrap manifest is the reviewed **initial import specification**, not a bot that continuously overwrites human refinement. Reruns reuse stable markers and leave existing issue bodies, checked tasks, assignments, states and dates untouched. Refinements belong in the issue and subsequent documentation PRs. Regenerate a new reviewed import only for genuinely new scope.

## Definition of Ready

A leaf is ready when the owner layer is unambiguous; the source call path has been revalidated; the desired behavior, exclusions and acceptance criteria are understood; blocking contracts have reached the required state; the containing SDK artifact is known for Client consumption; and the maintainer has accepted the PR scope. A lack of incoming graph edges alone does not make an issue ready.

Research may start before an implementation prerequisite is complete when its scope is discovery rather than mutation. Record that distinction; do not remove a real delivery blocker merely to make a board look unblocked. Artifact publication and live-host qualification are independent from source merge.

## Definition of Done

Require a focused reviewed PR, exact validation commands and results, updated contract/support documentation, compatibility notes, and evidence that cleanup, failures and relevant target/activation transitions work. For source-only or fixture-only delivery, retain the remaining live gate explicitly. A public interface, merged class, green documentation check or AOT executable publish does not establish live CE support.

An epic closes only when each child is completed with evidence or explicitly deferred through an accepted decision. A deferred capability is not delivered. The roadmap root is a navigation and acceptance record, not an issue every child PR should close.

## Branch and PR policy

Create one branch for a coherent leaf or clearly documented slice, not one per epic and not all proposed branches in advance. Target main through a PR. Preserve SDK CONTRIBUTING and AGENTS: focused imperative commits, no required Conventional Commit prefixes, no Co-authored-by trailers, no LINQ in C# production changes, and existing build/style rules. This bootstrap makes no protection or release-workflow changes.

A PR closes only the issue it actually completes. Cross-repository prerequisites are references and blockers, not collateral closing directives. The intended sequence is SDK contract PR, containing SDK package, Client adapter PR, then declared profile qualification. There is no automatic merge, version tag, release or package publication in the tooling.

## Priority and planning

P1 protects an affected reliability or compatibility promise. P2 is near-term alignment or capability delivery. P3 is optional/exploratory scope requiring deliberate prioritization. These are not security severity levels. No effort points, assignees, dates or delivery versions are invented. Dates should be added only after capacity and dependency commitments are agreed.

## Refinement cadence and exceptions

Review blocked work, package availability, live evidence and narrow PR scope during normal team refinement. Temporary Client mappings require a SDK owner, issue, replacement artifact and removal condition. Do not introduce an indefinite “Core may do anything” exception. Respect intentional raw SDK expert access without exposing raw state or owners through normal Client APIs.

## Sources

- [SDK contribution rules](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/CONTRIBUTING.md)
- [SDK repository guidelines](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/AGENTS.md)
- [Client architecture and live gates](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/README.md)
