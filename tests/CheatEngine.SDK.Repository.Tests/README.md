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
| `Documentation/`  | `DocumentationIntegrityTests` checks every Markdown file: links, anchors, paths, `docs/` pages.    |

Later work adds one folder per contract (for example `Documentation/`, `Workflows/`, `Qualification/`).

## Promise

- Every `*.csproj` on disk is built by CI through the solution, unless it is listed with a reason in
  `SolutionInventoryTests` (`Every_project_on_disk_is_in_the_solution_or_explicitly_excluded`).
- The solution lists no missing project and the exclusion list holds no stale entry
  (`Every_project_in_the_solution_exists_and_no_exclusion_is_stale`).
- Every relative link, image, reference definition and HTML `href`/`src` of every Markdown file resolves to a committed
  file or folder with GitHub's exact case, never above the repository root or into build output
  (`Every_relative_markdown_link_resolves_with_exact_casing`).
- Every `#fragment` matches a heading anchor (GitHub slug rules, duplicates suffixed) or an explicit anchor of its page
  (`Every_markdown_anchor_matches_a_heading_of_its_target_page`).
- No Markdown file contains an absolute local path such as a drive path, a `file:` URI or a user-profile folder
  (`No_markdown_file_contains_an_absolute_local_path`).
- No link points into the retired `documentations/` tree, and only `docs/README.md` names retired pages
  (`No_markdown_file_refers_to_the_retired_documentations_tree`).
- Absolute `blob/main` and `tree/main` links to this repository resolve on the current tree
  (`Absolute_links_to_this_repository_on_main_resolve_on_the_current_tree`), and so do the frozen links of the README
  published in CheatEngine.SDK 1.0.0 (`Links_of_the_published_1_0_0_package_readme_still_resolve_on_main`).
- Every README packed by a packable project uses absolute `https://` links only, and at least one is found
  (`Packed_readmes_contain_only_absolute_links`).
- Every page under `docs/` carries the "Recreated 2026-09" header
  (`Every_rebuilt_docs_page_starts_with_the_recreated_header`), every placeholder names its owning work and wave
  (`Placeholder_pages_name_their_owning_lot_and_wave`), and the docs index links every top-level page and folder
  (`Docs_index_links_every_top_level_page_and_folder`).
- No Markdown file claims complete coverage or universal support unless the same line negates it
  (`No_markdown_file_claims_complete_coverage_or_universal_support`).
- The Markdown parser and every rule are self-tested on in-memory pages, so each gate is shown to fail on the
  regression it exists for (`MarkdownDocumentTests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj
```
