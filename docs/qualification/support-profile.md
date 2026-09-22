# Support profile

> Recreated 2026-09 from the audit, not the historical documentations/ tree.

This page explains [`support-profile.json`](support-profile.json), the machine-readable list of the Cheat Engine host
profiles a qualification result can name. It follows the v0 schema
[`schemas/support-profile.v0.schema.json`](schemas/support-profile.v0.schema.json) and is checked by
`SupportProfileTests` in `tests/CheatEngine.SDK.Repository.Tests/Qualification`. Nothing here is a qualification
result: results live in the [matrix](README.md) and, for the exact host, in committed receipts.

## Profiles

| Profile id                           | Kind        | Qualifiable | Status      | Evidence kind        | What it identifies                                                                                                                                                                                   |
|--------------------------------------|-------------|-------------|-------------|----------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ce-public-src-ec45d5f`              | Documentary | no, never   | NotExecuted | `ObservedSource`     | The public `cheat-engine/cheat-engine` source at `ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`. It declares 7.5.1 and the historical string/CLR managed bootstrap. It explains behaviour; it proves nothing about the 7.7 binary. |
| `ce-7.7.0.10621-x64-managed-hostfxr` | Qualifiable | yes         | NotExecuted | `ExactInstalledFile` | `cheatengine-x86_64.exe` 7.7.0.10621, machine AMD64, SHA-256 `9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d`, loading managed plugins through nethost/hostfxr.                  |

The qualifiable profile also pins:

| Item                        | Value                                                                                                                                         |
|-----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------|
| Lua module                  | `lua53-64.dll`, SHA-256 `c95dcdfa0f60f97b43d970d77fd1bb907af4de04b500a3c89a99600b20b35bd2`, the same bytes as the committed `native/cheat-engine/lua53-64.dll` |
| Lua reference               | `celua.txt`, SHA-256 `aa1342b4a5d5d5c65b255fb3a8fd7b6bcbbac1cd138961669d9f37f43e0b9c00`, the reference the audit analysed                    |
| Load profile                | `managed-hostfxr`: the plugin is a framework-dependent .NET component started by Cheat Engine's nethost/hostfxr route                           |
| Runtime configuration       | `ce.runtimeconfig.json`, SHA-256 `68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b`, `LocalModified` (see [Runtime policy](#runtime-policy)) |
| SDK plugin contract version | `6`, the value the SDK writes through `GetVersion` (`libs/CheatEngine.SDK.Abi/AbiConstants.cs`). It is not the Cheat Engine product version 7.7. |
| Qualified backends          | `LocalProcess` only. A file opened as a process and CEServer produce different evidence and are not qualified (scenarios Q30.c and Q30.d).   |
| Authorized targets          | `tests/CheatEngine.SDK.QualificationTarget` for x64 and x86, published per run with its hash in each receipt; the installation's `gtutorial-i386.exe`, SHA-256 `9131b1ca916d6ac1fb67224a67cf0f578105d43aeeebb03137e94d73cbe11bca`, copied from the sandbox and never committed |
| Plugin architecture         | x64 only. A result on this profile authorizes no x86 or ARM64 plugin claim.                                                                     |

Excluded executables of the same installation: `cheatengine-x86_64-SSE4-AVX2.exe` (same FileVersion, different binary,
SHA-256 `9d861d651ab9d1dc3c09ae34c8ed5dee3d1a29b080784c3c48773494c9350230`, not profiled), the launcher
`Cheat Engine.exe` (FileVersion 6.3.0.0, SHA-256 `5313618d93640bb29b66baadf2339de85e593a51715290dadece6d58e039a75e`,
never used) and the x86 host `cheatengine-i386.exe` (SHA-256
`0a4b63eadbe824bcc5095a97ccdd8c580573ed72361514025abe1bf25f1387c9`, unsupported).

## Identity separation

These identities are never merged under a label such as "the latest Cheat Engine" (audit analyses/01, identity
table).

| Object                          | Identity                                                                               | What it can establish                                                                                   |
|---------------------------------|----------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------|
| Public Cheat Engine source      | `cheat-engine/cheat-engine` @ `ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37` (declares 7.5.1) | The behaviour of that source tree, profile `ce-public-src-ec45d5f`                                    |
| Controlled Cheat Engine binary  | `cheatengine-x86_64.exe` 7.7.0.10621, SHA-256 `9727076d…6eb4d`                          | Host behaviour, only through committed receipts on profile `ce-7.7.0.10621-x64-managed-hostfxr`        |
| SDK source under qualification  | The tree hash (`git rev-parse HEAD^{tree}`), pull request and head SHA each receipt records | The code a receipt ran; with squash merges the tree hash, not a branch commit, is the durable identity |
| SDK release tag `v1.0.0`        | Commit `a6fefb93e9c6f85a1bcedb68bf97e6741175b227`, tree `41678f939547b2215e106ee3bbc8c2878815652e` | The source the 1.0.0 package was built from; not by itself a proof of the package content              |
| SDK package consumed by the Client | `CheatEngine.SDK` 1.0.0, range `[1.0.0, 2.0.0)`                                     | The package the Client builds against today (see the tuples below)                                      |
| Lua reference                   | `celua.txt` SHA-256 `aa1342b4a5d5d5c65b255fb3a8fd7b6bcbbac1cd138961669d9f37f43e0b9c00`   | The documentary Lua reference; not the functions a running host exposes                                 |

## Package tuples

A compatibility result is tied to a tuple: package, native bridge, Lua module, exact host, runtime policy and load
profile (audit analyses/21, "Le tuple à qualifier"). Two tuples exist today and are kept apart.

**SDK-branch tuple.** The 2.0.0-alpha `CheatEngine.SDK` package built by the pull-request CI from the tree under
qualification. Each receipt records its identity: `nupkgSha256`, the NuGet content hash (SHA-512, base64) and the CI run.
Local packs are never qualification inputs. Its checked-in native bridge,
`native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll`, has SHA-256
`889dc4c231d182f9b7baa9e29880555aad949f42023dde232fe327542c3c5387` and source fingerprint
`3342be23f88976d9209a24bc0d8b9db512482a24d8db90381a836ea4f5595a56:2871368515be4c6fd235e49e793d5557e7c50229fcc8fbfd903efd39f9b754a8`
(SHA-256 of `cheatengine_sdk_lua_bridge.c` and of `xmake.lua`). When the bridge changes, this paragraph changes with it:
`SupportProfileTests` recomputes both values.

**Client-consumed SDK 1.0.0 tuple.** Three distinct package identities, each identifying something different:

| Identity                              | Value                                                                                        | What it identifies                                                                 |
|---------------------------------------|----------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------|
| Attested GitHub release asset SHA-256 | `99bf90101cd13e0183c94759e43badc6a1e719ffc3e2c9fd0b93490abdac0632`                           | The unsigned `.nupkg` the release workflow built and attested                      |
| NuGet `contentHash` (SHA-512, base64) | `n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==` | What consumer lock files hold (`libs/CheatEngine.Client.Core/packages.lock.json`)  |
| nuget.org repository-signed file SHA-256 | `3e8c98583ac71af25a5bd7053e7583fbafcd196139fae7c0b04bae9b40a7cd33`                        | The file nuget.org serves after adding its repository signature                    |

Its bridge `build/native/cheatengine-sdk-lua-bridge.dll` has SHA-256
`da08c2ba03019da3a8c432ef061d5d6133fd2169ba3a6a8e9ac903353856d994` and fingerprint
`8a63e00c7dd941212e7ef8c13d8c97f73142c5154bfbe5dbc5459e7131bb789b:2871368515be4c6fd235e49e793d5557e7c50229fcc8fbfd903efd39f9b754a8`.
The Client's own qualification records this tuple; a result on one tuple does not transfer to the other without an
argued `transferJustification`.

## Runtime policy

The inspected Cheat Engine 7.7 binary starts managed plugins through nethost/hostfxr. The `ce.runtimeconfig.json` of the
profiled installation targets `net10.0` with `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and
`Microsoft.AspNetCore.App` `10.0.0`, `rollForward` `LatestMinor`. It is a **local modification** (last written
2026-09-19, after the executable's 2026-06-16 date), not an installer baseline, and the installer's own configuration is
unknown.

- Editing that file is never harmless or universal: it changes the runtime of every managed plugin of the installation.
  Nothing in this repository edits an installed Cheat Engine, and no guide treats such an edit as a setup step.
- The qualification runner copies the installation into a sandbox and records the runtime configuration hash in every
  receipt; a different hash makes the run `NotApplicable`, never `Passed`.
- .NET runtimes observed on the qualification machine: x64 `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and
  `Microsoft.AspNetCore.App` 10.0.8, 10.0.11 and 10.0.12, plus x64 `Microsoft.NETCore.App` and
  `Microsoft.WindowsDesktop.App` 8.0.31; x86 `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App` 6.0.36 only. A
  framework-dependent `net10.0` x86 program therefore cannot run there, which is why the x86 qualification target is a
  Native AOT executable.

## Checkpoint A decisions

The audit's Checkpoint A (analyses/22) closes three decisions. Each is a `ProposedDecision` dated 2026-09-23 until the
maintainers confirm it.

| Id    | Decision                                                                                                                                                                                                                                                      |
|-------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| CPA-1 | Supported profile: `ce-7.7.0.10621-x64-managed-hostfxr`, Cheat Engine 7.7.0.10621 x64 with the locally modified runtime configuration above, disclosed as such. An installed Cheat Engine is never edited: the runtime configuration applies to every managed plugin. |
| CPA-2 | 2.0 ships the managed hostfxr route only. The classic native plugin route (`CEPlugin_*` exports, classic exports table) stays documentary, and the NativeAOT plugin route is unsupported (F02, Q41, Q42): a publish that succeeds is not a Cheat Engine load or unload. |
| CPA-3 | Conditional and optional API families (debugger, DBVM, speedhack, CEServer and the other deferred families of analyses/18) stay in the catalogue with explicit statuses and are outside the qualified scope of 2.0 (ADR-04, ADR-11).                        |

Consequences in the matrix: scenarios Q38 and Q39 are `NotApplicable` at C3 on this profile (no classic registration or
table route), Q42 is `NotApplicable` at C3 and C4 (no NativeAOT plugin route), and the file-as-process, CEServer, x86
host and ARM sub-rows (Q30.c, Q30.d, Q32.c, Q32.d) are `NotApplicable` at C3.

## Unsupported routes

| Route                    | Why                                                                                                                                                                                          |
|--------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `historical-clr-loader`  | The string-argument CLR bootstrap of the public source is a different load profile (ADR-02). The SDK never falls back to it.                                                                 |
| `nativeaot-plugin`       | A NativeAOT library cannot be unloaded with `FreeLibrary` ([Native AOT libraries](https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries)), which the classic loader calls. Unsupported until a residence model is qualified (F02, Q41, Q42). |
| `x86-host`               | `cheatengine-i386.exe` is an x86 process; the SDK and its native bridge are x64-only (Q32.c).                                                                                               |
| `sse4-avx2-host-variant` | `cheatengine-x86_64-SSE4-AVX2.exe` has the same version but a different binary; it is not profiled, so a result obtained with it qualifies nothing.                                        |

## Registry

Every copy of Cheat Engine, including the sandbox copy the runner starts and any instance the operator runs, reads and
writes the same `HKCU\Software\Cheat Engine` key. The profile lists, by name only, the subkeys and values that can
change a qualification run (for example `Plugins64`, where plugins registered through Settings > Plugins persist, and the
debugger and kernel-driver switches). Registry data is private: no export, value or datum is ever committed. The runner
exports the key before and after a run, records the difference as counts and value names in the receipt, and restores
it only when the difference is non-empty and no other Cheat Engine instance runs.

## Measurement record

The values above were measured on 2026-09-23, read-only, with `Get-FileHash -Algorithm SHA256` on the installed files
under `%ProgramFiles%\Cheat Engine` (evidence kind `ExactInstalledFile`) and on the committed repository files
(`ExactBinary`). They are kept apart from values that repository files only declare:

| Subject                                      | Declared before by                                                                                         | Measured evidence kind |
|----------------------------------------------|------------------------------------------------------------------------------------------------------------|------------------------|
| `cheatengine-x86_64.exe`                     | `tests/CheatEngine.SDK.LiveProbe/LiveProbeAuthorization.cs`, `tests/CheatEngine.SDK.LivePlugin/README.md` | `ExactInstalledFile`   |
| installed `lua53-64.dll`                     | `native/cheat-engine/README.md`                                                                            | `ExactInstalledFile`   |
| committed `native/cheat-engine/lua53-64.dll` | `native/cheat-engine/README.md`, `BundledLuaLibraryTests`                                                   | `ExactBinary`, recomputed by `SupportProfileTests` |
| `ce.runtimeconfig.json`                      | nothing (the LivePlugin guide only states that it is a local modification)                                   | `ExactInstalledFile`   |
| `celua.txt`                                  | the audit's identity table                                                                                 | `ExactInstalledFile`   |
| excluded executables, `gtutorial-i386.exe`   | nothing                                                                                                    | `ExactInstalledFile`   |

A measured file is still not a host observation. The executable hash becomes `ObservedHost` only in a dated receipt that
ran on it; until then every C3/C4 cell of the matrix is `NotExecuted`.

## Not executed

The list of missing evidence (audit Checkpoint A deliverable): every matrix row with at least one level still
`NotExecuted`. It is generated from [`matrix.json`](matrix.json) and checked by
`SupportProfileTests.Not_executed_section_equals_the_matrix`.

<!-- BEGIN GENERATED: not-executed -->
<!-- END GENERATED: not-executed -->
