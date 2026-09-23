# CheatEngine.SDK.Repository.Tests

## Objective

Keep the repository's own contracts true: the solution inventory today, and the documentation, workflow,
qualification-matrix and catalogue contracts added by the audit remediation work.

## Why it exists

Several of these rules used to live in scripts that CI stopped running, so they silently rotted (dead
`documentations/` links, orphaned validators). Repository rules are enforced by C# tests instead, and they stay fast:
this project only reads committed files. It never builds, packs or restores. The one exception is `Governance/`: it
starts `pwsh` to run the repository's PowerShell policy code (the pull request policy module and entry script, the
health-check rules, the repository-settings plan) against test vectors, offline.

## How it works

| Folder            | Content                                                                                            |
|-------------------|----------------------------------------------------------------------------------------------------|
| `Infrastructure/` | `RepositoryRoot` finds `CheatEngine.SDK.slnx` above the test binaries and enumerates source files. |
| `Solution/`       | `SolutionInventoryTests` compares the projects on disk with the projects listed in the solution.   |
| `Documentation/`  | `DocumentationIntegrityTests` checks every Markdown file: links, anchors, paths, `docs/` pages.    |
| `Toolchain/` | `ToolchainPinTests` reads `global.json`, `Directory.Build.props` and `Directory.Solution.targets`: exact SDK, analysis-level pin, NuGet audit policy. |
| `LockFiles/` | `LockFileTests` mirror the structural checks of `eng/Update-LockFiles.ps1` over the committed `packages.lock.json` files. |
| `PublicApi/` | PublicAPI files, `CompatibilitySuppressions.xml` and the `eng/api/*.txt` lists: file shape, declared breaks, Client-induced breaks, enum contracts. |
| `Workflows/` | `WorkflowContractTests` parse `.github/workflows/*.yml` and the composite actions with YamlDotNet and freeze the CI contract; `CoverageBaselineTests`, `BuildInfoSchemaTests` and `ClientCanaryScriptTests` check the files and scripts of `eng/ci/` that CI runs. |
| `Governance/` | Pull request policy script and workflow, CodeQL/Scorecard/zizmor/dependency-submission/scheduled-health workflow invariants, Dependabot, CODEOWNERS, SECURITY.md, issue forms and repository-settings payloads. |

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
- The required check `CI / Gate` keeps its shape: the three callers call `ci.yml` through job `ci` named `CI`, the gate
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
- No workflow listens to `pull_request_target` or `merge_group`, the pull-request and policy workflows filter no path,
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
- The Release leg packs before it tests and hands the exact nupkg to the packaging tests; the Debug leg excludes them
  by trait, never by skip; every module runs once with hang and crash dumps well inside the job timeout and is checked by
  the inventory (`Release_leg_packs_before_testing_and_exports_the_exact_nupkg`,
  `Debug_leg_excludes_packaging_tests_by_trait_never_by_skip`, `Test_step_runs_every_module_once_with_the_contract_options`,
  `Every_test_module_references_the_extensions_the_test_step_uses`,
  `Hang_dump_timeout_is_well_below_the_build_test_job_timeout`, `Test_module_inventory_runs_in_both_legs`,
  `Build_test_runs_both_configurations_without_fail_fast`).
- Artifacts use the reserved names and retentions only, binary logs and dumps are uploaded on failure only and never
  from Sonar or release runs, jobs that version a package fetch full history, the Native AOT probes are published, and
  the live probe is compiled exactly once and never shipped (`Every_uploaded_artifact_name_is_reserved`,
  `Binlogs_are_uploaded_only_on_failure_and_never_from_sonar_or_release`, `Jobs_that_pack_or_test_fetch_full_history`,
  `Aot_job_publishes_the_native_aot_probes`, `Live_probe_is_compiled_by_the_ci_solution_build`).
- actionlint, zizmor and PSScriptAnalyzer are pinned by version and checksum, every zizmor exception carries its reason,
  and the format job verifies whitespace without a restore (`Lint_job_checks_out_the_repository_and_runs_every_linter`,
  `Zizmor_and_actionlint_are_pinned_by_version_and_checksum`, `Every_zizmor_exception_carries_a_justification_comment`,
  `Script_analysis_uses_a_pinned_hash_verified_psscriptanalyzer`, `Format_job_verifies_whitespace_without_restore`).
- The dependency review never skips and reviews pull requests only, and the lock-file job verifies the committed locks
  on Windows (`Dependency_review_job_always_runs_and_reviews_only_pull_requests`,
  `Dependency_review_configuration_blocks_advisories_and_unreviewed_licenses`,
  `Lock_file_job_runs_the_verification_script_on_windows`).
- No workflow runs the local qualification runner or generates ApiCompat suppressions
  (`No_workflow_references_the_local_qualification_runner`, `No_workflow_passes_ApiCompatGenerateSuppressionFile`).
- The coverage floors cover exactly the shipping assemblies, are percentages with an explicit tolerance, use the
  pinned merge tool, and CI never writes them (`Coverage_baseline_lists_exactly_the_shipping_assemblies`,
  `Coverage_floors_are_percentages_and_the_tolerance_is_explicit`, `Coverage_tool_is_pinned_in_the_local_tool_manifest`,
  `Debug_leg_checks_the_coverage_floors_and_never_writes_the_baseline`).
- `build-info.json` has exactly the contract fields, rejects any other, is written in full from the native job's
  outputs and uploaded by the Release leg (`Build_info_schema_requires_exactly_the_contract_fields`,
  `Build_info_schema_rejects_additional_properties`, `Build_info_writer_emits_every_required_field`,
  `Release_leg_writes_and_uploads_build_info_from_the_native_job_outputs`).
- The advisory client canary writes the fields of its report schema, isolates the branch package and never fails the
  run because the Client breaks (`Client_canary_report_schema_requires_exactly_the_fields_the_script_writes`,
  `Client_canary_isolates_its_packages_and_never_gates`).
- Every Dependabot ecosystem waits at least seven days before proposing a release, the Roslyn pin and the SDK-implicit
  packages never move on their own, the `dotnet-sdk` ecosystem ignores new majors, the composite action is updated with
  the workflows, and no ecosystem sets a commit prefix (`DependabotConfigurationTests`:
  `Every_ecosystem_has_a_cooldown_of_at_least_seven_days`, `Roslyn_pins_and_sdk_implicit_packages_are_ignored`,
  `Roslyn_ignores_cover_every_package_pinned_to_the_roslyn_floor`, `Dotnet_sdk_ecosystem_ignores_major_updates`,
  `Github_actions_updates_cover_the_composite_action_directories`, `No_ecosystem_sets_a_commit_message_prefix`,
  `Specific_nuget_groups_come_before_the_catch_all_group`).
- The `PR policy` required check evaluates the title (at most 72 characters, no trailing period, no type or area prefix,
  uppercase start, imperative first word) and the CHANGELOG entry for `libs/`, `src/`, `analyzers/`, `source-generators/`
  and `native/` changes (lock files excluded, waiver marker, Dependabot exempt), against vectors that include real pull
  request titles; the entry script annotates each failed rule, writes the summary table and never prints the description
  (`PullRequestPolicyScriptTests`: `Policy_verdict_matches_the_expected_rules`, `Every_rule_is_exercised_by_a_failing_vector`,
  `Dependabot_authored_pull_requests_are_exempt_from_every_rule`, `Changelog_failure_names_the_paths_and_both_remedies`,
  `Entry_script_exits_non_zero_and_annotates_each_failed_rule`, `Entry_script_writes_a_rule_table_to_the_step_summary`,
  `Entry_script_exempts_dependabot_and_never_prints_the_description`,
  `Entry_script_refuses_commit_ids_that_are_not_full_hashes`).
- `pr-policy.yml` runs on every title edit without path filter or condition, as the job `PR policy` on `ubuntu-24.04`,
  and pull-request text reaches scripts only through `env:` in every workflow (`PullRequestPolicyWorkflowTests`:
  `Pr_policy_triggers_on_edited_and_has_no_path_filter`, `Pr_policy_job_is_named_PR_policy_and_runs_on_ubuntu_24_04`,
  `Pull_request_title_body_and_author_reach_the_script_only_through_env`,
  `Changelog_path_pattern_matches_the_shared_contract`, `Every_consumer_visible_root_of_the_changelog_rule_exists`).
- `SECURITY.md` names private reporting, scope, response targets, supported versions and release verification, and
  describes the two committed binaries with their real hash; `CODE_OF_CONDUCT.md` routes reports through private
  reporting without an e-mail address; `.github/CODEOWNERS` is the only CODEOWNERS file, starts with `*`, names known
  maintainers and existing paths with exact case (`GovernanceDocumentTests`:
  `Security_policy_names_private_reporting_scope_response_and_supported_versions`,
  `Security_policy_describes_the_committed_binaries_with_their_real_hash`,
  `Code_of_conduct_routes_reports_through_private_reporting_without_an_email_address`,
  `Codeowners_patterns_point_to_existing_paths_and_known_owners`, `Codeowners_exists_only_in_the_github_folder`).
- The compatibility issue form requires the complete support tuple (package version and lock-file `contentHash`,
  bridge hash, Cheat Engine build and executable hash, Lua DLL hash, runtime-configuration hash and origin, load
  profile, target architecture, what was actually run from C0 to C4, build options, OS and .NET), never presents a
  profile as supported or qualified, and no form tells users to edit `ce.runtimeconfig.json`; blank issues are off and
  the chooser links private reporting (`GovernanceDocumentTests`: `Compatibility_issue_form_requires_the_full_tuple`,
  `Compatibility_form_never_presents_a_profile_as_qualified_or_supported`,
  `Issue_form_element_ids_are_unique_and_valid`, `Issue_forms_disable_blank_issues_and_link_private_reporting`,
  `Issue_forms_never_instruct_editing_the_cheat_engine_runtime_configuration`).
- Every governance workflow pins its actions by full SHA with a version comment, uses literal `windows-2025` or
  `ubuntu-24.04` runners with timeouts, starts from `contents: read` and comments every job-level elevation, never
  persists checkout credentials, checks the exit code of every native command and script it runs, never uses
  `pull_request_target`, `merge_group` or a package cache, and uploads only its reserved artifact names
  (`GovernanceWorkflowTests`: `Governance_workflows_pin_every_action_by_full_sha_with_a_version_comment`,
  `Governance_jobs_use_literal_runner_labels_and_timeouts`, `Governance_workflows_start_read_only_and_comment_every_job_elevation`,
  `Governance_checkouts_never_persist_credentials`, `Governance_scripts_check_the_exit_code_of_every_native_command`,
  `Advisory_workflows_never_use_pull_request_target_or_merge_group`, `Governance_workflows_never_enable_a_package_cache`,
  `Governance_workflows_upload_only_their_reserved_artifact_names`).
- CodeQL analyses C# from a manual, traced, non-incremental Release build of the shipped product graph without the
  compiler server, C/C++ and the workflows without a build, with no dependency or TRAP cache, on pull requests, `main`,
  a weekly schedule and dispatch (`GovernanceWorkflowTests`: `Codeql_analyzes_csharp_cpp_and_actions_with_literal_runner_labels`,
  `Codeql_csharp_job_builds_the_product_graph_manually_without_shared_compilation`,
  `Codeql_workflow_never_enables_a_package_cache`, `Codeql_runs_on_pull_requests_main_a_weekly_schedule_and_dispatch`).
- Scorecard keeps the shape its publication verifier accepts (no `defaults`, `env` or `run` steps, allowlisted actions,
  an Ubuntu runner), only its analysis job and the release workflow request an OIDC token, and the online zizmor run
  pins the tool version the Gate uses, enables the online audits and skips forks and drafts (`GovernanceWorkflowTests`:
  `Scorecard_workflow_has_no_defaults_env_or_run_steps`, `Scorecard_steps_use_only_the_actions_the_verifier_allows`,
  `Only_the_scorecard_job_requests_an_id_token`, `Zizmor_online_pins_the_tool_version_and_enables_online_audits`,
  `Online_and_gate_zizmor_runs_pin_the_same_version`).
- Dependency submission detects on a read-only token with a pinned, hash-verified Component Detection over the locked
  restore, and submits from a separate job that is the only governance job holding `contents: write`, runs no
  third-party code and refuses a snapshot of another commit, ref or correlator; it runs on `main`, dispatch and
  same-repository pull requests only (`GovernanceWorkflowTests`:
  `Dependency_submission_runs_on_main_dispatch_and_same_repository_pull_requests_only`,
  `Only_the_dependency_submit_job_holds_contents_write`, `Dependency_submit_job_runs_no_third_party_code`,
  `Dependency_detection_uses_a_pinned_hash_verified_component_detection`,
  `Dependency_submit_step_submits_only_a_snapshot_of_this_run`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj
```
