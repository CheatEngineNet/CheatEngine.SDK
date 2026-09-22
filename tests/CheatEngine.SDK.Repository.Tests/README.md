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
| `Toolchain/` | `ToolchainPinTests` reads `global.json`, `Directory.Build.props` and `Directory.Solution.targets`: exact SDK, analysis-level pin, NuGet audit policy. |
| `LockFiles/` | `LockFileTests` mirror the structural checks of `eng/Update-LockFiles.ps1` over the committed `packages.lock.json` files. |
| `PublicApi/` | PublicAPI files, `CompatibilitySuppressions.xml` and the `eng/api/*.txt` lists: file shape, declared breaks, Client-induced breaks, enum contracts. |

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
- The .NET SDK is pinned exactly: `rollForward: disable`, no prerelease, and an `errorMessage` naming the pinned version
  and its install command (`Global_json_requires_the_exact_sdk_with_roll_forward_disabled`,
  `Global_json_error_message_names_the_pinned_sdk_version`).
- The analysis level is a release-shaped pin that moves with the SDK major and minor, and no other MSBuild file sets it
  (`Analysis_level_is_pinned_to_a_release_not_latest`, `Analysis_level_pin_moves_with_the_pinned_sdk_major_and_minor`,
  `No_project_or_props_file_overrides_the_pinned_analysis_level`).
- High and critical NuGet advisories fail every restore, CI solution restores assert that every project was audited,
  and advisory suppressions live only in `Directory.Build.props` with a justification and an expiry
  (`Nuget_audit_blocks_high_and_critical_advisories_in_every_build`,
  `Ci_solution_restores_assert_that_nuget_audit_covered_every_project`,
  `Nuget_audit_suppressions_live_in_the_root_props_with_a_justification_and_an_expiry`).
- Each of the six shipping libraries, and nothing else, has both PublicAPI files, each starting with
  `#nullable enable` and ordinally sorted without duplicates; Shipped never carries a removal marker, and every
  `*REMOVED*` line repeats a Shipped line exactly (`Every_shipping_library_has_both_public_api_files`,
  `Public_api_files_exist_only_next_to_shipping_libraries`, `Every_public_api_file_starts_with_nullable_enable`,
  `Every_public_api_file_is_ordinally_sorted_after_its_header`, `Shipped_files_never_contain_removed_markers`,
  `Every_removed_line_names_a_line_of_the_shipped_file`, `Unshipped_never_redeclares_a_live_shipped_line`).
- The ApiCompat baseline suppressions, the `*REMOVED*` lines and the reviewed list of changes ApiCompat cannot see
  describe the same breaks against 1.0.0, and the suppressions that touch a Client-consumed type are exactly the listed
  Client-induced breaks; SDK-side C0 evidence for Q48 only
  (`Every_suppression_is_a_baseline_suppression_of_one_library_against_itself`,
  `Every_baseline_suppression_matches_a_removed_public_api_line`,
  `Every_removed_public_api_line_is_suppressed_or_declared_invisible_to_apicompat`,
  `Every_invisible_change_names_a_current_removed_line_with_a_reason`,
  `Suppressions_touching_client_consumed_types_are_listed_as_induced_client_breaks`,
  `Every_client_consumed_type_resolves_in_the_declared_api_or_is_marked_unresolved`).
- Enums that mirror Cheat Engine constants or appear in Client signatures keep their 1.0.0 members, every enum added
  since 1.0.0 is classified, and status/outcome enums start with a neutral zero member, except a pending list that can
  only shrink (`Enums_mirroring_cheat_engine_constants_or_client_signatures_keep_their_1_0_0_members`,
  `Every_enum_added_after_1_0_0_is_classified`, `Status_and_outcome_enums_added_after_1_0_0_do_not_default_to_success`,
  `Pending_zero_value_fixes_are_still_needed`).
- Every project, inside or outside the solution, has a lock file in NuGet's version 2 format, ending as NuGet writes it;
  Native AOT projects lock their runtime-specific ILCompiler package; no lock resolves a CheatEngine.* package from a feed
  (`Every_project_has_a_committed_lock_file`, `Lock_files_parse_and_declare_a_supported_format_version`,
  `Every_lock_file_is_version_2_because_every_project_uses_central_package_management`,
  `Version_1_lock_files_hold_no_central_transitive_entries`, `Native_aot_projects_lock_the_win_x64_ilcompiler_packages`,
  `No_lock_file_resolves_a_cheatengine_package`, `Lock_files_end_without_a_final_newline_as_nuget_writes_them`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj
```
