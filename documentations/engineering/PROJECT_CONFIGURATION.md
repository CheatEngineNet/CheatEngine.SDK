# Organization Project Configuration

## One shared execution Project

Use one organization Project, **CheatEngineNet — Engineering Execution**, containing issues from both repositories. Maintain separate SDK and Client table views rather than duplicating every issue into independently maintained planning boards. Each repository retains its own seven milestones and roadmap root. This document is a versioned operating specification; inspect the live Project to establish its current state.

This PR intentionally ships no Project-mutating script. A future operator tool must be introduced in its own reviewed change, with a dry-run/readback mode, idempotency rules, a mutation receipt, and explicit permissions. Avoid concurrent refinement during an import because GitHub GraphQL writes do not provide a compare-and-swap guarantee. Repository delivery remains usable when Project permissions are unavailable.

## Fields

Use built-in Status and repository/milestone data. Add CE Planning ID, CE Layer, CE Phase, CE Priority, CE Readiness, CE Start and CE Target. Readiness begins at Needs refinement; having no blocker does not automatically imply Ready. Contract, artifact and live gates are tracked explicitly in issues and reviewed before changing readiness. Dates remain empty until real capacity and sequencing commitments exist.

## Saved views and remaining UI configuration

| View | Layout | Selection | Purpose |
|---|---|---|---|
| SDK Backlog | Table | SDK repository | Group by milestone; show priority, readiness and dependencies. |
| Client Backlog | Table | Client repository | Show SDK blockers and exact package gates. |
| Cross-repository Board | Board | Open issues | Group by built-in Status; do not duplicate state into several label schemes. |
| Blocking Contracts | Table | Open issues | Display native dependency fields and separate artifact blockers. |
| Roadmap | Roadmap | Epic label | Use CE Start / CE Target only after dates are accepted. |

Grouping, sorting, roadmap date-field binding and Project workflow automation remain explicit UI checks. This PR deliberately does not claim a checked-in Project view manifest or automated saved-view provisioning; verify native saved views after applying changes rather than treating Project existence as proof. GitHub roadmap visualization requires date information; an undated outcome plan must not manufacture dates just to fill a chart.

## Operating guidance

Keep completed issues in the Project unless the team approves archival behavior. Avoid automatic closure of epics or cross-repository issues on a child merge. Keep unsafe/unqualified capability work visible as deferred or gated, not silently removed. Do not add a new organization issue type globally just to represent an epic; the bootstrap uses ordinary issues, a dedicated label, and native hierarchy.

## Sources

- [Project creation](https://cli.github.com/manual/gh_project_create)
- [Field creation](https://cli.github.com/manual/gh_project_field-create)
- [Item editing](https://cli.github.com/manual/gh_project_item-edit)
- [Roadmap layout](https://docs.github.com/en/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/customizing-the-roadmap-layout)
