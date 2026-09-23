# CheatEngine.SDK.Repository.Tests

## Objective

Keep the repository's own contracts true: the solution inventory, the CI workflow contract, lock files, the public
API surface and the release workflow.

## Why it exists

Repository rules are enforced by C# tests instead of custom scripts, and they stay fast: this project only reads
committed files (solution, workflows, Dependabot configuration, issue forms, community documents). It never builds,
packs or restores. The one exception is one Governance/ test, which starts `pwsh` to run the dependency-submission
workflow's own "submit" step text against a mocked `gh`, offline.

## How it works

| Folder            | Content                                                                                                |
|--------------------|--------------------------------------------------------------------------------------------------------|
| `Infrastructure/`  | `RepositoryRoot` finds `CheatEngine.SDK.slnx` above the test binaries and enumerates source files.     |
| `Solution/`        | `SolutionInventoryTests` compares the projects on disk with the projects listed in the solution; `QualificationHarnessShapeTests` checks the shape of the two qualification-harness projects. |
| `Toolchain/`       | `ToolchainPinTests` reads `global.json`, `Directory.Build.props` and `Directory.Solution.targets`: exact SDK, analysis-level pin, NuGet audit policy. |
| `LockFiles/`       | `LockFileTests` mirror the structural checks a locked restore relies on, over the committed `packages.lock.json` files. |
| `PublicApi/`       | `PublicApiFileTests` and `EnumContractTests` check the shape of every shipping library's PublicAPI files and the classification of enums added since 1.0.0. |
| `Workflows/`       | `WorkflowContractTests` parse `.github/workflows/*.yml` and the composite actions with YamlDotNet and freeze the CI contract (job ids, the Gate, lint, format, restore, supply-chain jobs). |
| `Release/`         | `ReleaseWorkflowContractTests` reads `.github/workflows/release.yml`: the draft-first job chain, tag guards, write scopes, trusted publishing placement and the reserved artifact names. |
| `Governance/`      | `GovernanceWorkflowTests` freeze CodeQL, Scorecard, the online zizmor run and dependency submission; `DependabotConfigurationTests` checks `.github/dependabot.yml`; `GovernanceDocumentTests` checks `SECURITY.md`, `CODE_OF_CONDUCT.md`, `.github/CODEOWNERS` and the issue forms. |

## Promise

- Every `*.csproj` on disk is built by CI through the solution, unless it is listed with a reason in
  `SolutionInventoryTests` (`Every_project_on_disk_is_in_the_solution_or_explicitly_excluded`).
- The solution lists no missing project and the exclusion list holds no stale entry
  (`Every_project_in_the_solution_exists_and_no_exclusion_is_stale`).
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
- The required check `CI / Gate` keeps its shape: the caller calls `ci.yml` through job `ci` named `CI`, the gate
  job `gate` named `Gate` runs `always()` with no permissions, needs every other job except the advisory allowlist, and
  decides from a required result per job; the `ci.yml` jobs, inputs and secret are exactly the contract's
  (`Callers_invoke_ci_through_job_ci_named_CI`, `Gate_job_is_named_Gate_runs_always_and_has_no_permissions`,
  `Gate_needs_every_other_ci_job_except_the_advisory_allowlist`, `Ci_jobs_match_the_frozen_contract_ids_and_names`,
  `Ci_declares_exactly_the_contract_inputs_and_secret`).
- Sonar is required exactly when `SONAR_EXPECTED` says so: the job condition and the gate expression are the same text,
  the quality gate is awaited outside push events, and non-product trees are excluded
  (`Sonar_condition_equals_the_gate_sonar_expected_expression`, `Sonar_waits_for_the_quality_gate_outside_push_events`,
  `Sonar_excludes_non_product_trees_from_analysis_and_coverage`,
  `Pull_request_and_main_callers_request_sonar_and_the_release_run_never_does`).
- No workflow listens to `pull_request_target` or `merge_group`, the pull-request workflow filters no path,
  main keeps every run and pull requests cancel superseded ones
  (`No_workflow_uses_pull_request_target_or_a_merge_group_trigger`, `Pull_request_and_policy_workflows_have_no_path_filters`,
  `Main_ci_runs_every_push_to_main_without_a_concurrency_group`, `Pull_request_ci_skips_drafts_and_cancels_superseded_runs`).
- Every job runs on `windows-2025` or `ubuntu-24.04` with a timeout, workflows grant read permissions only at the top
  level and the pipeline never elevates, every remote action is pinned to a commit with its version, every checkout drops
  its credentials, every native command checks its exit code, no run script interpolates an expression, and the
  pipeline scripts run in `pwsh` (`Every_job_has_a_timeout_and_a_pinned_runner_label`,
  `Workflows_grant_only_read_permissions_at_the_top_level`, `Pipeline_jobs_never_elevate_permissions`,
  `Every_remote_action_is_pinned_to_a_full_sha_with_a_version_comment`, `Every_checkout_disables_credential_persistence`,
  `Every_native_command_in_a_workflow_script_checks_its_exit_code`, `No_run_script_interpolates_an_expression`,
  `Pipeline_workflows_and_composite_actions_run_scripts_in_pwsh`).
- Every dotnet job installs the pinned SDK through the composite action, every restore is locked, and no job reachable
  from a release, Sonar or CodeQL run uses a package cache (`Every_dotnet_job_uses_the_composite_setup_action`,
  `Composite_setup_restores_in_locked_mode`, `Every_restore_in_the_pipeline_is_locked`,
  `Release_reachable_workflows_never_enable_a_package_cache`, `Sonar_restores_locked_from_nuget_org_before_the_scanner_begins`).
- The Release leg packs before it tests and hands the exact nupkg to the packaging tests, with an inlined pre-publish
  sanity check (one package, its nuspec identity, the embedded SBOM and the CI-built native bridge); the Debug leg
  excludes packaging tests by trait, never by skip; every module runs once with hang and crash dumps well inside the
  job timeout (`Release_leg_packs_before_testing_and_exports_the_exact_nupkg`,
  `Debug_leg_excludes_packaging_tests_by_trait_never_by_skip`, `Test_step_runs_every_module_once_with_the_contract_options`,
  `Every_test_module_references_the_extensions_the_test_step_uses`,
  `Hang_dump_timeout_is_well_below_the_build_test_job_timeout`, `Build_test_runs_both_configurations_without_fail_fast`).
- Artifacts use the reserved names and retentions only, binary logs and dumps are uploaded on failure only and never
  from Sonar or release runs, jobs that version a package fetch full history, the Native AOT probes are published, and
  the live probe is compiled exactly once and never shipped (`Every_uploaded_artifact_name_is_reserved`,
  `Binlogs_are_uploaded_only_on_failure_and_never_from_sonar_or_release`, `Jobs_that_pack_or_test_fetch_full_history`,
  `Aot_job_publishes_the_native_aot_probes`, `Live_probe_is_compiled_by_the_ci_solution_build`).
- actionlint and zizmor are pinned by version and checksum, every zizmor exception carries its reason, and the format
  job verifies whitespace without a restore (`Lint_job_checks_out_the_repository_and_runs_every_linter`,
  `Zizmor_and_actionlint_are_pinned_by_version_and_checksum`, `Every_zizmor_exception_carries_a_justification_comment`,
  `Format_job_verifies_whitespace_without_restore`).
- The dependency review never skips and reviews pull requests only, and the lock-file job restores the solution and
  every out-of-solution project in locked mode on Windows (its own `--locked-mode` restore is the verification, with
  no bespoke script) (`Dependency_review_job_always_runs_and_reviews_only_pull_requests`,
  `Dependency_review_configuration_blocks_advisories_and_unreviewed_licenses`,
  `Lock_file_job_restores_the_solution_and_every_out_of_solution_project_locked_on_windows`).
- No workflow runs a local qualification runner or generates ApiCompat suppressions
  (`No_workflow_references_the_local_qualification_runner`, `No_workflow_passes_ApiCompatGenerateSuppressionFile`).
- The release workflow is draft-first (`verify → ci → attest → draft-release → publish → verify-publication →
  finalize-release`), runs for `v*.*.*` tags and manual dry runs without cancelling a run in progress, and calls `ci.yml`
  with the tag version, a 90-day retention and no Sonar (`Release_jobs_form_the_draft_first_chain`,
  `Release_runs_for_version_tags_and_manual_dry_runs_without_cancelling`,
  `Release_calls_ci_with_the_tag_version_ninety_day_retention_and_no_sonar`).
- Publication jobs and every attestation step run only for tags of this repository; the attest job attests the package
  provenance and its SPDX 2.2 SBOM extracted from the package's own embedded manifest; only `publish` uses the `nuget`
  environment, with the NuGet login right before the push; `id-token`, `attestations` and `contents` write scopes are
  limited to the jobs that need them (`Publication_jobs_run_only_for_tags_of_this_repository`,
  `Attest_job_attests_the_package_provenance_and_its_spdx_2_2_sbom`,
  `Only_the_publish_job_uses_the_nuget_environment_and_nuget_login`, `Id_token_write_is_limited_to_attest_and_publish`,
  `Contents_write_is_limited_to_draft_release_and_finalize_release`).
- The release is created as a draft by `draft-release` only and published by `finalize-release` only, after
  `verify-publication` polled and byte-compared the nuget.org copy against the attested package, and the package is
  checked against `SHA256SUMS` before the push (`Release_is_created_as_a_draft_and_published_only_by_finalize`,
  `Publication_is_verified_on_nuget_org_before_the_release_is_published`).
- Release jobs use pinned runners with timeouts, upload only `release-notes` and `attestation-bundles` (never a second
  copy of the nupkg), and install the pinned SDK without a package cache where they run `dotnet`
  (`Release_jobs_have_a_timeout_and_a_pinned_runner`, `Release_uploads_only_reserved_artifact_names`,
  `Release_jobs_that_run_dotnet_install_the_pinned_sdk_and_never_cache_packages`).
- The advisory governance workflows (CodeQL, Scorecard, the online zizmor run, dependency submission) pin every
  action, use literal runner labels and timeouts, start read-only and comment every job elevation, never persist
  checkout credentials, start every multi-line script with `$ErrorActionPreference = 'Stop'`, check the exit code of
  every native command, never use `pull_request_target`/`merge_group`/`workflow_run`, never enable a package cache,
  and upload only their reserved artifact names (`GovernanceWorkflowTests`). CodeQL analyses C# from a manual,
  traced Release build of the product graph without the compiler server, C/C++ and the workflows without a build.
  Scorecard keeps the shape its publication verifier accepts and is the only advisory job besides `release.yml` that
  requests an OIDC token. Dependency submission detects on a read-only token with a pinned, hash-verified Component
  Detection scan inlined directly in the workflow, and submits from a separate job that is the only governance job
  holding `contents: write`, runs no third-party code, and is proven end to end (with `gh` replaced by a recorder) to
  refuse a snapshot of another commit, ref or correlator.
- Every Dependabot ecosystem waits at least seven days before proposing a release, the Roslyn pin and the
  SDK-implicit packages never move on their own, the `dotnet-sdk` ecosystem ignores new majors, the composite action
  directory is covered, and no ecosystem sets a commit prefix (`DependabotConfigurationTests`).
- `SECURITY.md` names private reporting, scope, response targets, supported versions and release verification, and
  describes the two committed binaries with their real hash; `CODE_OF_CONDUCT.md` routes reports through private
  reporting without an e-mail address; `.github/CODEOWNERS` is the only CODEOWNERS file, starts with `*` and names
  known maintainers and existing paths; the compatibility issue form requires the complete support tuple, never
  presents a profile as supported or qualified, and no form tells users to edit `ce.runtimeconfig.json`
  (`GovernanceDocumentTests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj
```
