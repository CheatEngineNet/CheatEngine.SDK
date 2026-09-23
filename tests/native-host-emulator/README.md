# native-host-emulator

A native hostfxr host, built and run outside .NET, that plays the coexistence A/B protocol against the two managed
`CheatEngine.SDK.LivePlugin.Coexistence` plugins through the same managed (hostfxr) load path Cheat Engine uses. It
is C2 evidence (SDK-COEX-1, F03, Q09) for a real `.NET hosting` component load: **it is not Cheat Engine's loader**,
and its facts must never be read as an executed Cheat Engine result.

## Objective

Measure, from a real hostfxr host, what a managed plugin load actually does when two independently built plugin
assemblies that both depend on `CheatEngine.SDK.Hosting` are loaded side by side: whether they land in the same or a
different `AssemblyLoadContext`, whether their `CheatEngine.SDK.Hosting` static state (the `PluginHost` instance) is
shared or separate, and what happens to the second plugin when it is. Q09 C4 (an exact Cheat Engine host, two SDK
copies, receipts) remains required and separate; this program only ever produces C2 evidence.

## Why it exists

`docs/…/matrix.json` and this repository do not exist without a bundled qualification runner any more (the
maintainer's docs/eng pivot), and Checkpoint B's local Cheat Engine run is cancelled for this branch. The coexistence
question (does the loader used by Cheat Engine give two plugin DLLs separate SDK statics, or one shared instance) is
still answerable *for the hostfxr component-hosting route* without Cheat Engine at all, because that route is
publicly documented .NET hosting behaviour
(<https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting>) that a native program can drive directly. That
is exactly what this program does, and only that: it never claims Cheat Engine's own loader uses this route.

## How it works

`ce_host_emulator.cpp` (C++20, MSVC x64, `/W4 /WX /EHsc /O2 /DUNICODE`):

1. Loads `--lua` (the repository's bundled `lua53-64.dll`) with `LoadLibraryW`, so it resolves under its own module
   name for the plugins' own dependent load, and creates one `lua_State` (`luaL_newstate` + `luaL_openlibs`). No Lua
   headers are vendored (same choice as `native/cheatengine-sdk-lua-bridge`): the handful of Lua 5.3 C API entry
   points this protocol needs are declared locally and resolved with `GetProcAddress`.
2. Locates `hostfxr` with `nethost`'s `get_hostfxr_path`, using an explicit `--dotnet-root` (never the environment,
   never the registry, never an installed Cheat Engine), then calls `hostfxr_initialize_for_runtime_config` against
   `ce-like.runtimeconfig.json` — **one runtime for the whole process** (only one `.NET` runtime can ever be loaded
   per process; the consumer tests run one emulator process per scenario).
3. Resolves each plugin's bootstrap entry point one of two ways, selected by `--alc`:
   - `component` (the well-documented route): `hdt_load_assembly_and_get_function_pointer` on the plugin's own
     assembly path. This is Microsoft Learn's documented component-hosting route, and it isolates each assembly path
     into its own `AssemblyLoadContext` — **measured** below, not assumed.
   - `default` (not documented on Microsoft Learn; declarations came from the installed SDK's
     `Microsoft.NETCore.App.Host.win-x64` pack `hostfxr.h`/`coreclr_delegates.h`, never a guess): `hostfxr_set_runtime_property_value`
     sets `APP_PATHS` to the plugins' shared directory *before* the first runtime delegate is requested (properties
     can only be set before the runtime loads), then `hdt_load_assembly` + `hdt_get_function_pointer` load both
     plugins into the **default** `AssemblyLoadContext`.
4. Calls the resolved `CESDK.CESDK.CEPluginInitialize(IntPtr, int)` bootstrap **twice** (Cheat Engine's own documented
   name-query-then-load sequence) into a 36-byte `PluginInitRecord` surrounded by 64 guard bytes (`0xCD`) on each
   side, and checks that the guard bytes and the `Name` pointer are unchanged afterwards.
5. Calls `GetVersion`, then `EnablePlugin` with a `ManagedExportedFunctions` record whose five slots are implemented
   by this program (`GetLuaState` returns the one Lua state; `LuaRegister` is a stub that only counts a call, since
   the managed side documents it as "do not call"; `LuaPushClassInstance` pushes a light userdata; `ProcessMessages`
   is a no-op; `CheckSynchronize` always reports one dispatched call).
6. Runs the coexistence protocol over Lua: enable A, then B; call each plugin's own
   `cheatengine_sdk_coexistence_{a,b}_identity()`/`_ping()`; disable A and confirm A's globals go `nil` while B (if
   enabled) keeps answering; re-enable A and confirm a new `Epoch`; disable B then A and confirm every global is
   `nil`.
7. Writes an ordered `key=value` facts file to `--facts`. Every value is redacted for absolute paths before it is
   written (`ALC` names of isolated component load contexts embed the component's assembly path), so the facts file,
   and everything derived from it, never carries a local path.

`ce_host_emulator_abi.h` declares the same 36-byte `PluginInitRecord` / 48-byte `ManagedExportedFunctions` /
16-byte `PluginVersion` shapes as `CheatEngine.SDK.Abi`, with `static_assert` checks on every size and offset. It is
intentionally **not** `tests/native-abi-fixture/ce77_plugin_abi_contract.h`: that header is S-ABI's, and this program
is a separate C2 evidence artifact with its own facts.

`ce-like.runtimeconfig.json` mirrors the qualifiable host profile's runtime shape (`net10.0`;
`Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App`, `Microsoft.AspNetCore.App`, each `10.0.0` /
`LatestMinor`) exactly as recorded, and is never copied from an installed Cheat Engine.

## Build it

```powershell
. <scratchpad>\tools\Enter-CeEnv.ps1 -Lot <your-lot>
./tests/native-host-emulator/build.ps1 -OutputDirectory artifacts/native-host-emulator
```

`build.ps1` locates an x64 Visual Studio Developer environment (the same `VsDevCmd.bat` pattern as
`tests/native-abi-fixture/build.ps1`, with an optional `-VcToolsVersion` pin), locates `nethost.h`, `nethost.lib`,
`nethost.dll`, `hostfxr.h` and `coreclr_delegates.h` under the newest `Microsoft.NETCore.App.Host.win-x64` pack of
`-DotNetRoot` (default: the `dotnet.exe` on `PATH`), compiles `ce-host-emulator.exe`, copies `nethost.dll` and the
runtimeconfig next to it, and writes `native-host-emulator.manifest.txt` (SHA-256 of the exe, `nethost.dll`, the
runtimeconfig and every source file, plus the observed MSVC toolset, Windows SDK and pack versions). It checks
`$LASTEXITCODE` after every native command.

## Promise

- The ABI header's `static_assert` lines match `CheatEngine.SDK.Abi`'s managed record sizes and offsets
  (`Emulator_abi_header_declares_the_managed_record_sizes` in `CheatEngine.SDK.Hosting.Tests`).
- Both bootstrap calls write the guarded 36-byte record without touching a byte outside it, and the `Name` pointer
  is the same pointer on both calls (`Bootstrap_canaries_and_name_pointers_stay_intact_for_both_plugins`).
- The component route measures each plugin's `PluginHost` static identity and Lua-visible behaviour under separate
  and shared output folders (`Separate_folders_measure_distinct_hosting_instances_and_disabling_A_leaves_B_callable`,
  `Shared_folder_under_the_component_route_is_measured_and_disabling_A_leaves_B_callable`).
- The default-ALC route's rejection of the second plugin is measured, not asserted from documentation
  (`Default_context_loading_rejects_the_second_plugin_deterministically`).
- Re-enabling the first plugin after a disable answers under a new `Epoch` (`Reenabling_A_answers_with_a_new_epoch`).
- No fact and no exit path ever contains an absolute path (`Emulator_facts_contain_no_absolute_path`), a
  `C:\Program Files\Cheat Engine` reference, or `cheatengine-x86_64` (`NativeHostEmulatorScriptTests`, Repository
  project).
- `build.ps1` checks `$LASTEXITCODE` after `cl.exe`, never reads an installed Cheat Engine, and writes only under its
  `-OutputDirectory` (`NativeHostEmulatorScriptTests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Hosting.Tests/CheatEngine.SDK.Hosting.Tests.csproj -c Debug --no-build --fail-skips on
dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj -c Debug --no-build --filter-class "*NativeHostEmulatorScriptTests" --fail-skips on
```

`CESDK_NATIVE_HOST_EMULATOR_DIR` (absolute path to this script's `-OutputDirectory`) and
`CESDK_NATIVE_HOST_EMULATOR_REQUIRED` (`true`/`false`) gate `NativeHostEmulatorTests` exactly like the native ABI
fixture's own `CE77_NATIVE_ABI_FACTS_PATH`/`CE77_NATIVE_ABI_REQUIRED` pair: unset locally, the emulator-dependent
tests return after asserting that documented opt-out; `REQUIRED=true` without the directory is an actionable error,
never a silent skip.

## Formerly known local dependency (now fixed)

`tests/CheatEngine.SDK.LivePlugin.Coexistence/CoexistencePlugin.props` builds both plugins through a direct
`ProjectReference` chain, never through the packaged `CheatEngine.SDK` NuGet's `build/CheatEngine.SDK.props` asset.
That packaged asset was the **only** place in this repository that set `CheatEngineSdkGenerateEntryPoint=true` and
registered it as a `CompilerVisibleProperty`; without both, the `CheatEngine.SDK.SourceGenerators.EntryPoint`
generator stayed silent (by its own documented design — see
`source-generators/CheatEngine.SDK.SourceGenerators.EntryPoint/README.md`, "an indirect package reference leaves it
absent and produces no bootstrap") and neither plugin assembly contained a `CESDK.CESDK` type. hostfxr's
`load_assembly_and_get_function_pointer` therefore never resolved an entry point to call, so this program's own
`plugin.entryResolved` stayed `false` and every fact that depends on a call into the plugin (`{a,b}.bootstrap.*`,
`{a,b}.getversion`, `{a,b}.enable.*`) reported `skipped` — not `failed`, because `boolText()` reports `skipped`
whenever the call itself was never attempted. `CoexistencePlugin.props` now carries the same two-line opt-in the
packaged consumer gets for free:

```xml
<PropertyGroup>
  <CheatEngineSdkGenerateEntryPoint>true</CheatEngineSdkGenerateEntryPoint>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="CheatEngineSdkGenerateEntryPoint" />
</ItemGroup>
```

With the fix landed, `load_assembly_and_get_function_pointer` resolves the bootstrap for both plugins and every
`AssertBootstrapAndEnableSucceeded` assertion reports `ok`/`true` instead of `skipped`: all 9
`NativeHostEmulatorTests` pass with `CESDK_NATIVE_HOST_EMULATOR_DIR`/`REQUIRED=true` set.
