# CheatEngine.SDK.Repository.Tests

## Objective

Keep the repository's own contracts true: the solution inventory today, and the documentation, workflow,
qualification-matrix and catalogue contracts added by the audit remediation work.

## Why it exists

Several of these rules used to live in scripts that CI stopped running, so they silently rotted (dead
`documentations/` links, orphaned validators). Repository rules are enforced by C# tests instead, and they stay fast:
this project only reads committed files. It never builds, packs, restores or starts a process.

## How it works

| Folder            | Content                                                                                            |
|-------------------|----------------------------------------------------------------------------------------------------|
| `Infrastructure/` | `RepositoryRoot` finds `CheatEngine.SDK.slnx` above the test binaries and enumerates source files. |
| `Solution/`       | `SolutionInventoryTests` compares the projects on disk with the projects listed in the solution.   |

Later work adds one folder per contract (for example `Documentation/`, `Workflows/`, `Qualification/`).

## Promise

- Every `*.csproj` on disk is built by CI through the solution, unless it is listed with a reason in
  `SolutionInventoryTests` (`Every_project_on_disk_is_in_the_solution_or_explicitly_excluded`).
- The solution lists no missing project and the exclusion list holds no stale entry
  (`Every_project_in_the_solution_exists_and_no_exclusion_is_stale`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj
```
