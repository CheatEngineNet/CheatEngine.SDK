# Local qualification protocol

> Recreated 2026-09 from the audit, not the historical documentations/ tree.

How an operator produces exact-host (C3/C4) evidence for the [qualification matrix](README.md): the prerequisites, the
safety rules, how to obtain the exact package, the procedure of each scenario, and how receipts are reviewed, committed
and linked from the matrix. The runner itself is documented in [`eng/qualification`](../../eng/qualification/README.md).
Nothing in this protocol runs in CI, and a C1/C2 result is never a substitute for it.

## Prerequisites

| Need                  | Detail                                                                                                                                                              |
|-----------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Host                  | Windows x64 with the installation of profile `ce-7.7.0.10621-x64-managed-hostfxr` ([support profile](support-profile.md)); the runner's preflight verifies every hash. |
| Tools                 | PowerShell 7.4 or later, the .NET SDK that `global.json` pins, the MSVC build tools Native AOT needs (for the qualification target), git, and the GitHub CLI.     |
| Repository            | A clean checkout of the tree the package was built from: its `git rev-parse HEAD^{tree}` goes into every receipt.                                                   |
| Package               | The exact CI package of that tree (below), or the nuget.org file for a released version. A local pack is only good for a smoke run.                               |
| Operator              | Your GitHub handle (`-Operator`), recorded in every receipt, and about one hour of attention for the guided scenarios.                                              |

## Safety rules

- **Never in CI.** The runner exits with code 3 when `CI`, `GITHUB_ACTIONS` or `TF_BUILD` is set, and no workflow
  references it (`LocalQualificationRunnerTests`).
- **Never elevated.** Run PowerShell as your normal user; Cheat Engine then starts as the invoking user.
- **Sandbox only.** The installation under `%ProgramFiles%\Cheat Engine` is read and copied, never written, launched or
  configured. The runner mirrors it into `<WorkRoot>\sandbox`, compares every file by SHA-256, and starts
  `cheatengine-x86_64.exe` from there; never the launcher or the SSE4-AVX2 executable.
- **Registry.** Every copy of Cheat Engine shares `HKCU\Software\Cheat Engine`. The runner exports it before and after
  each scenario, records the difference as counts and value names, and restores it only when the difference is
  non-empty. If another Cheat Engine instance runs, stop it first; with `-AllowOtherCheatEngineInstances` the runner never
  restores automatically and stops with exit code 7 and the manual restore command instead.
- **One lab at a time.** Runs take the `Global\ce-lab` mutex, so two runners (or a runner and another lab tool that
  honours the mutex) never share Cheat Engine.
- **Quiet machine.** No heavy build while timings are recorded. The runner refuses when `dotnet`, `MSBuild` or
  `VBCSCompiler` run, unless `-AllowConcurrentLoad` marks the timings indicative.
- **Disposable targets only.** The qualification target, or the installation's `gtutorial-i386.exe` for x86
  scenarios; nothing else is opened.

## Obtaining the exact package

1. Find the CI run of the pull request head (or of `main`) whose tree you have checked out.
2. Download its package artifact (name `nuget-package`):

   ```powershell
   gh run download <runId> --repo CheatEngineNet/CheatEngine.SDK -n nuget-package -D <folder>
   ```

3. The runner reads the id and version from the package's `.nuspec`, never from the file name, and records three
   identities in each receipt: the SHA-256 of the `.nupkg`, the NuGet content hash (SHA-512, base64) computed by an
   isolated restore, and the CI run URL. It builds every plugin from that package only, with an isolated NuGet packages
   folder, so a package already in your global NuGet cache can never be used instead.

## Running

Check the host first; this reads and hashes files and starts nothing:

```powershell
./eng/qualification/Invoke-LocalQualification.ps1 -PreflightOnly -WhatIf
```

Then run the scenarios, all of Checkpoint B or a list:

```powershell
./eng/qualification/Invoke-LocalQualification.ps1 -Scenario CheckpointB -PackagePath <folder>\CheatEngine.SDK.<version>.nupkg `
    -CiRunUrl https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/<runId> -PullRequest <number> -HeadSha <sha> -Operator <handle>
```

Each scenario is one Cheat Engine session. When a scenario needs you, the runner prints `OPERATOR STEP` with the action
to take in Cheat Engine and waits; type your observation (or `y` / `n` when asked) and press Enter. The Lua driver then
continues. A plumbing check with a local pack uses `-Smoke`: it writes `smoke-report.json` and never a receipt.

## Scenarios

The plan is [`eng/qualification/scenarios.json`](../../eng/qualification/scenarios.json); preconditions, operation and
expected result come from the [matrix](matrix.json). Every LiveProbe scenario opens the x64 qualification target first
and loads the plugin with `loadPlugin`, so the probe's authorization gate (exact host, short-lived manifest, target
opened in Cheat Engine) is satisfied.

| Scenario | Level | Harness                       | Target | Steps                                                                                                                    | Pass rule                                                                                         |
|----------|-------|-------------------------------|--------|--------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------|
| Q02      | C3    | LiveProbe                     | x64    | Automated status read; operator reviews the tail-canary record                                                           | canary written and operator confirms only the 36 record bytes were written                         |
| Q03      | C3    | LiveProbe                     | x64    | Automated status read                                                                                                    | records the exports size the host reports; `NotApplicable` (a reduced table cannot be produced)    |
| Q04      | C3    | LiveProbe                     | x64    | Automated status read                                                                                                    | raw second bootstrap integer recorded, interpretation `none`                                       |
| Q05      | C3    | LiveProbe                     | x64    | Status, operator unticks and ticks the plugin, status again, operator confirms the name                                  | new epoch, same plugin assembly, stable name                                                       |
| Q05.a    | C3    | LiveProbeNonAscii             | x64    | Status; operator records how the name is shown and whether the list is intact                                            | plugin loads and the operator confirms nothing is corrupted                                        |
| Q06      | C3    | LiveProbe, fault `OnEnable`   | x64    | Load with the fault switch; the runner removes it; operator re-enables                                                   | first enable failed and its commands are gone; re-enable succeeds and records `OnEnable@1`        |
| Q07      | C3    | LiveProbe                     | x64    | Operator unticks the plugin while `ce77_live_probe_pump_messages(20)` runs                                               | pump completes and the plugin stays enabled (nested disable refused); operator confirms the action |
| Q08      | C3    | LiveProbe, fault `OnDisable`  | x64    | Operator unticks (OnDisable throws), the runner removes the switch, operator re-enables and judges                       | failure recorded (`OnDisable@1`) and no cleanly-disabled state shown while cleanup had failed       |
| Q09.a    | C4    | Coexistence A and B, one folder | none | Load both, record identities, operator removes A, B still answers, operator restores A                                   | identities recorded; B works while A is removed; both work again                                   |
| Q09.b    | C4    | Coexistence A and B, two folders | none | Same as Q09.a                                                                                                           | same                                                                                               |
| Q14      | C3    | LiveProbe                     | x64    | Automated: `pcall(ce77_live_probe_throw_managed_exception)`, then status                                                 | catchable Lua error with the marker text; the next call works                                      |
| Q15      | C3    | LiveProbe                     | x64    | Callback prepared and called; operator unticks; the kept callback is called again                                        | the kept callback fails with "released"                                                            |
| Q17      | C3    | —                             | —      | Manual: no SDK-controlled state replacement can be triggered in the host yet                                             | stays `NotExecuted`                                                                                |
| Q18      | C3    | LiveProbe                     | x64    | Snapshot, operator runs `resetLuaState()`, snapshot after; the driver resumes from its progress file                     | operator confirms the outcome is recorded as unsupported, with no safety claimed                   |
| Q19      | C3    | LiveProbe                     | x64    | Worker Lua-state and synchronize observations; operator judges                                                           | operator confirms serialization or refusal                                                         |
| Q39      | C3    | —                             | —      | None: `NotApplicable` by profile decision CPA-2                                                                          | —                                                                                                  |
| Q40      | C3    | LiveProbe (clean folder)      | x64    | Status and host profile                                                                                                  | plugin, Hosting assembly and bridge load from the bundle folder; bridge equals the packaged one    |

A scenario whose operator step was not performed (for example Q07 without unticking) is **Inconclusive**: no receipt is
written and the runner says so.

## LiveProbe authorization and fault switch

For every LiveProbe scenario the runner writes the short-lived `ce77-live-probe-v1` manifest (exact host hash, the
target's PID and image hash, `disposable: true`, 30-minute expiry) under the run directory, sets
`CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT` and `CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE` in its own process so that Cheat Engine
inherits them, and deletes the manifest and the variables when the session ends. For Q06 and Q08 it also writes
`liveprobe.fault.json` (`{"schema":"ce77-live-probe-fault-v1","throwIn":"OnEnable"}` or `OnDisable`) next to the plugin
in the bundle folder and removes it at the operator step that re-enables the plugin. See the
[LiveProbe harness](../../tests/CheatEngine.SDK.LiveProbe/README.md).

## Reviewing, publishing and linking receipts

1. Receipts are written to `<WorkRoot>\runs\<runId>\receipts\<Qid>\<receiptId>.json`, each with
   `<receiptId>.events.json`. Read the `observed` text and the event log: user and machine names, local paths outside
   the placeholders (`<workRoot>`, `<sandbox>`, `<ceInstall>`, `<repo>`, `<bundle:NAME>`, `<target:ARCH>`, `<user>`,
   `<machine>`, `<userProfile>`), and raw debug output must not appear. A smoke report flags a user path that escaped
   redaction (`unredactedUserPath`).
2. Copy them into the repository with `-PublishReceiptsTo <repository>` (or by hand) as
   `docs/qualification/receipts/<Qid>/<receiptId>.json` and `.events.json`. The runner prints the matrix cell update.
3. Update the matching cell of [`matrix.json`](matrix.json) in the same commit: `status` and `passKind` as in the
   receipt, `evidenceKind: ObservedHost`, an `evidence` entry `{kind: Receipt, receiptId, path, sha256}` (the SHA-256 of
   the receipt after CRLF is normalized to LF), `treeHash`, `nupkgSha256` and `date`. A NotApplicable receipt (Q03) keeps
   its justification.
4. Freshness: a receipt is valid for the tree and package it names. A cell whose `treeHash` or `nupkgSha256` differs
   from its receipt's needs a `transferJustification` that says why the result still holds; without one the repository
   tests refuse the cell. Replace the generated blocks of `README.md` and `support-profile.md` with the text the failing
   tests print.
5. `dotnet test --project tests/CheatEngine.SDK.Repository.Tests` validates the receipts, their event logs, the matrix
   links and the hashes before the pull request is opened.

## Never committed

Cheat Engine or target binaries, the authorization manifest, the fault switch, registry exports (`.reg` files hold
private settings), bundle folders, the driver's raw `driver-events.jsonl`, smoke reports, and raw DebugView output. Only
the redacted receipt and its event log enter the repository.
