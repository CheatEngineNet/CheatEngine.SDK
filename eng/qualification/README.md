# eng/qualification

The local, operator-attended runner that produces exact-host (C3/C4) receipts for the
[qualification matrix](../../docs/qualification/README.md). The operator's side is the
[local qualification protocol](../../docs/qualification/local-protocol.md).

## Objective

Run a qualification scenario on a sandbox copy of the exact Cheat Engine host with plugins built from the exact CI
package, and write a redacted, schema-valid receipt that a reader can check without the operator's machine (audit
analyses/20, ADR-12). The runner never runs in CI and never modifies the Cheat Engine installation or leaves the
operator's Cheat Engine settings changed.

## How it works

| File                                          | Content                                                                                                                                                  |
|-----------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Invoke-LocalQualification.ps1`               | The runner: guard, preflight, mutex, sandbox, bundles, targets, registry, Cheat Engine session, receipts.                                                |
| `QualificationRunner.psm1`                    | Pure helpers the runner and the tests share: hashing with the LF rule, registry parsing and name-only diff, redaction, event log bounding, receipt id and assembly, Lua literals, bundle closure, restored content hash, pass-rule evaluation. |
| `driver/zz_cesdk_qualification.template.lua`  | The autorun Lua driver template: runs one scenario step per timer tick under `pcall`, appends one JSON event per line, resumes after a Lua state reset. |
| `scenarios.json`                              | The Checkpoint B plan: harnesses, target, steps (Lua or Operator), observed values and a declarative pass rule per scenario.                           |

### Stages

1. **Guard.** The first statement: exits 3 when `CI`, `GITHUB_ACTIONS` or `TF_BUILD` is set, before anything else.
2. **Preflight** (read-only). Hashes `cheatengine-x86_64.exe`, `lua53-64.dll`, `ce.runtimeconfig.json` and `celua.txt`,
   reads the file version and PE machine and compares them with [`support-profile.json`](../../docs/qualification/support-profile.json);
   refuses an elevated runner, another Cheat Engine instance, build processes and a dirty tree unless allowed; checks
   that `dotnet --version` equals `global.json`; records `dotnet --list-runtimes`.
3. **Mutex** `Global\ce-lab` (an abandoned mutex is taken over).
4. **Sandbox.** `robocopy /MIR` of the installation into `<WorkRoot>\sandbox`, then a SHA-256 comparison of every file;
   a stale driver is removed.
5. **Bundles.** For each harness (`LiveProbe`, `LiveProbeNonAscii`, `LivePlugin`, `CoexistenceA`, `CoexistenceB`): a
   throw-away consumer project with empty `Directory.Build.*` and `Directory.Packages.props`, a copy of `global.json`,
   `Compile` items for the harness sources, a `PackageReference` to the exact package version read from its `.nuspec`, a
   `NuGet.Config` with `<clear/>` and package source mapping, and an isolated `NUGET_PACKAGES`; restored with
   `--no-http-cache --force-evaluate`, published, then checked: plugin with a static `CESDK.CESDK` class, public or
   internal as the package's entry-point generator emits it, and a public static `CEPluginInitialize(nint, int)` (read
   with System.Reflection.Metadata), the six SDK assemblies, a `.deps.json` whose only `project` library is the plugin's
   own root entry, that lists `CheatEngine.SDK/<version>` as a `package` and holds no absolute path,
   `.runtimeconfig.json`, and the bridge equal to the package's `build/native` copy. Q09.a merges A and B into one
   folder and refuses a same-named file with different bytes. Each bundle gets `bundle-manifest.<name>.json` (every file
   with its SHA-256, and the build warnings).
6. **Package and bridge identity.** SHA-256 of the `.nupkg`; the NuGet content hash from the isolated restore's
   `.nupkg.metadata` (the lock-file value, which differs from a SHA-512 of the file bytes for a signed package); SHA-256
   of the packaged bridge and its exported source fingerprint.
7. **Targets.** Publishes `tests/CheatEngine.SDK.QualificationTarget` for the needed architectures, starts it and reads
   its ready record; writes the LiveProbe authorization manifest and, for fault scenarios, `liveprobe.fault.json`.
8. **HKCU before.** `reg export` of `HKCU\Software\Cheat Engine` into the session folder.
9. **Driver.** Generates `autorun\zz_cesdk_qualification.lua` in the sandbox from the template with the steps, bundle
   paths and target PIDs.
10. **Launch.** Starts the sandbox `cheatengine-x86_64.exe`, not elevated, and serves operator steps through handshake
    files; the watchdog (`-CeTimeoutSeconds`) kills Cheat Engine and fails the scenario.
11. **Cleanup** (always): stops the target and stray sandbox processes, removes the driver, the manifest, the fault
    switch and the environment variables.
12. **HKCU after.** Exports, compares by value names, restores only a non-empty difference with no other Cheat Engine
    running, and verifies the restore (exit 7 otherwise, with the manual command and the backup path).
13. **Redaction and receipt.** Replaces the work root, sandbox, bundles, repository, installation, user and machine
    names with placeholders, bounds the event log, evaluates the pass rule and writes `<receiptId>.json` and
    `<receiptId>.events.json` (receipt id `R-<start UTC>-<Qid>-<first 8 hex of the package SHA-256>`).
14. **Publication** (`-PublishReceiptsTo`): copies receipts into `docs/qualification/receipts/<Qid>/` and prints the
    matrix cell update. The runner never edits `matrix.json`.

### Parameters

| Parameter                          | Meaning                                                                                                   |
|------------------------------------|-----------------------------------------------------------------------------------------------------------|
| `-Scenario <string[]>`             | Scenario ids of `scenarios.json` (`Q04`, `Q09.a`, …) or `CheckpointB` (default) for every runnable one; a comma-separated string (`pwsh -File … -Scenario Q04,Q14`) is split. |
| `-PackagePath <path>`              | The exact `CheatEngine.SDK` `.nupkg`. Required unless `-PreflightOnly`.                                    |
| `-PackageSource CiArtifact\|NuGetOrg` | Where the package came from (default `CiArtifact`).                                                    |
| `-CiRunUrl <url>`                  | The CI run that produced the artifact; required for `CiArtifact` receipts.                                |
| `-PullRequest <n>`, `-HeadSha <sha>` | Pull request identity recorded in receipts (both or neither).                                           |
| `-Operator <handle>`               | GitHub handle recorded in receipts; required for receipts.                                                |
| `-CheatEnginePath <path>`          | The installation to copy; default `%ProgramFiles%\Cheat Engine`. Read and copied only.                   |
| `-WorkRoot <path>`                 | Default `%LOCALAPPDATA%\CheatEngineNet\qualification`; refused inside a git work tree or below the repository's parent directory. |
| `-CeTimeoutSeconds <n>`            | Watchdog per scenario session (default 900).                                                              |
| `-MutexTimeoutMinutes <n>`         | How long to wait for `Global\ce-lab` (default 30).                                                        |
| `-PreflightOnly`                   | Stage 2 only; prints the preflight record and exits 0 when the host matches the profile.                  |
| `-Smoke`                           | Plumbing run with a local pack: `smoke-report.json`, never a receipt; a dirty tree is allowed.           |
| `-AllowConcurrentLoad`             | Run despite build processes; receipts mark the timings indicative.                                        |
| `-AllowOtherCheatEngineInstances`  | Run next to another Cheat Engine; HKCU is then never restored automatically.                              |
| `-PublishReceiptsTo <repository>`  | Copy the receipts into that repository's `docs/qualification/receipts`.                                   |
| `-WhatIf`, `-Confirm`              | Standard `ShouldProcess` switches; `-PreflightOnly -WhatIf` changes nothing.                               |

### Exit codes

| Code | Meaning                                                                 |
|------|-------------------------------------------------------------------------|
| 0    | Success (receipts or smoke report written, or preflight passed)         |
| 2    | Invalid arguments                                                       |
| 3    | A CI environment variable is set                                        |
| 4    | The installation is not the profiled host                               |
| 5    | Unsafe environment (elevation, other instance, load, dirty tree, SDK version, work root, sandbox copy, mutex) |
| 6    | Cheat Engine, build, driver or any other unhandled failure (for example the watchdog fired or a harness did not build); cleanup, HKCU restore and the mutex release still run |
| 7    | HKCU restore or verification failure: the backup path and the command are printed |

### Files written

Everything goes under `-WorkRoot`: `sandbox\` (the verified copy, reused between runs) and `runs\<runId>\` with
`package\` (feed and extracted bridge), `nuget-packages\`, `build\<harness>\`, `bundles\<harness>\`,
`bundle-manifest.<harness>.json`, `target-<arch>\`, `sessions\<Qid>\` (registry exports, authorization manifest,
driver events, handshakes, progress) and `receipts\<Qid>\`, or `smoke-report.json`. The registry exports and the
authorization manifest contain private data and must never be committed.

## Promise

- The guard runs before any side effect and refuses every CI marker
  (`LocalQualificationRunnerTests.Runner_refuses_to_run_under_CI_before_any_side_effect`,
  `Runner_guard_is_the_first_statement_and_the_script_runs_in_strict_mode`).
- No command, file API or robocopy destination of the runner targets the installation
  (`Runner_never_writes_to_the_Cheat_Engine_source_directory`), and no workflow references the runner
  (`No_workflow_references_the_local_qualification_runner`).
- A receipt assembled from a recorded event log is valid against the v0 schema and the receipt rules
  (`Receipt_builder_produces_a_schema_valid_receipt_from_a_recorded_event_log`); redaction removes user paths and keeps
  scenario values (`Redaction_removes_user_paths_and_keeps_scenario_values`); registry differences carry names only
  (`Registry_diff_reports_value_names_only`); an incomplete bundle is refused
  (`Bundle_closure_check_rejects_a_missing_bridge_or_a_workspace_project_entry`) and a bundle built from the package,
  with the generated internal entry point, is accepted
  (`Bundle_closure_check_accepts_a_package_consumer_bundle_with_the_generated_internal_entry_point`); the recorded
  content hash is the
  lock-file value (`Content_hash_is_the_lock_file_value_the_restore_recorded_not_the_file_bytes_hash`); only Cheat
  Engine's own executables count as another instance, never a process such as a `CheatEngine.*` test host
  (`Only_Cheat_Engine_executables_count_as_another_instance`).
- Every Checkpoint B scenario names a matrix cell and only Lua functions its harnesses declare
  (`Every_Checkpoint_B_scenario_exists_and_cites_harness_commands_that_exist`), and every generated driver compiles
  with Cheat Engine's Lua 5.3 module (`Driver_templates_are_valid_Lua`).
- PSScriptAnalyzer reports no warning or error for this folder.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj --filter-class "*LocalQualificationRunnerTests"
Invoke-ScriptAnalyzer -Path eng/qualification -Recurse -Severity Warning,Error
```

The tests need PowerShell 7 (`pwsh`) on `PATH`; they fail rather than skip without it.
