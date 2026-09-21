# ADR-006: Keep the managed plugin route; defer NativeAOT plugin loading

**Status:** Accepted for the managed route; NativeAOT Cheat Engine plugin loading is explicitly deferred.

**Work item:** [SDK-006](work-items/SDK-006.md)<br />
**Decision date:** 2026-09-21<br />
**Scope:** Windows x64 Cheat Engine plugin loading only. This is not a statement about NativeAOT executable publication.

## Decision

The supported Cheat Engine.SDK plugin profile remains the existing managed, framework-dependent deployment. A
successful NativeAOT publish is not a supported Cheat Engine plugin profile, and it is not evidence that Cheat Engine
can disable, remove, or unload a NativeAOT plugin.

NativeAOT plugin loading is therefore **deferred**, rather than advertised as supported. The repository may inspect a
test-only NativeAOT library and may load that library in a short-lived, dedicated process under the bounded harness
below. It must not load the library in Cheat Engine, call an exported function, activate a plugin, attach to a target,
or attempt an unload.

The SDK remains the sole owner of any future native entry points. The test fixture deliberately exports only
`CheatEngineSdkNativeAotProbe_*` names, not the `CEPlugin_*` names that Cheat Engine recognizes. Neither this decision
nor the harness adds a Client bootstrap, changes the generated managed entry point, or adds a production native plugin
entry point.

## Evidence and constraints

The following facts are deliberately kept separate:

| Gate | Evidence | What it establishes | What it does not establish |
| --- | --- | --- | --- |
| Library analysis | `CheatEngine.SDK.NativeAotLoaderHarness --analyze` reads the PE export directory without mapping the DLL. | The test library is PE32+ AMD64 and contains its two test-only export names. | Loader behavior, export invocation, activation, a CE profile, or unloading. |
| Executable publication | `CheatEngine.SDK.AotProbe` publishes and runs as a standalone executable. | The rooted shipping SDK graph passed that publish/run invocation. | Native-library loading, CE plugin discovery, activation, coexistence, or unloading. |
| Controlled test-library load | The harness maps only its freshly published inert probe DLL in its own process, requires an acknowledgement switch, and performs name queries only. | This one process could map that known test DLL and resolve its two names. | That Cheat Engine can load it, that the exports have a CE-compatible lifecycle, or that it can be unloaded. |
| Live native plugin loading | No run is authorized or recorded. | Nothing. | A supported NativeAOT plugin profile. |

The pinned upstream source is [Cheat Engine `plugin.pas` at
`ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin.pas).
Its source-level loader path calls `LoadLibrary`, resolves `CEPlugin_GetVersion`,
`CEPlugin_InitializePlugin`, and `CEPlugin_DisablePlugin` (with legacy-name fallbacks), retains an `HMODULE` for an
installed plugin, and calls `FreeLibrary` when removing it. The matching [pinned C
header](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin/cepluginsdk.h)
declares those three classic exports as `__stdcall` functions returning `BOOL`.

That is source evidence, not a measured Cheat Engine 7.7 binary profile. It nevertheless conflicts with the relevant
NativeAOT limitation: Microsoft states that a NativeAOT shared library exposes methods marked
`UnmanagedCallersOnly` with a non-null entry point, and that unloading through `FreeLibrary` or `dlclose` is not
supported. See [Building native libraries](https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries).
No `NativeLibrary.Free`, `FreeLibrary`, or equivalent operation exists in the loader harness.

The checked-in packaging tests inspect a `.nupkg` directly as a ZIP and use isolated NuGet package directories; they
do not extract a NativeAOT library and are not loader evidence. Likewise, the two-plugin fixture merged in PR #56
observes the managed loader's exact assembly/load-context identities only. It remains a manual managed-host protocol,
not an AOT test.

## Bounded harness

`tests/CheatEngine.SDK.NativeAotLibraryProbe` publishes a deliberately inert shared NativeAOT library. Its exports are
never called. `tests/CheatEngine.SDK.NativeAotLoaderHarness` has these rules:

1. `--analyze` uses `PEReader` and the export directory in file bytes. It never maps the DLL.
2. `--load` accepts no file path. It can map only the fixed fixture file placed beside its own published executable;
   CI publishes both components into a new profile directory. The harness opens that fixture with a
   replacement-blocking read lock before it inspects or maps anything, and it refuses a DLL that exposes a
   `CEPlugin_` name. The lock remains held while the same hashed bytes are mapped.
3. `--load` maps the profile-adjacent probe inside the harness process, resolves only the test export addresses, and
   reports the SHA-256. It never invokes an address and intentionally never frees the handle. Process termination, not
   an unload API, ends the controlled observation.

Use the three gates independently:

```powershell
dotnet publish tests/CheatEngine.SDK.AotProbe/CheatEngine.SDK.AotProbe.csproj -c Release

dotnet publish tests/CheatEngine.SDK.NativeAotLibraryProbe/CheatEngine.SDK.NativeAotLibraryProbe.csproj -c Release -o artifacts/nativeaot-loader-profile
dotnet publish tests/CheatEngine.SDK.NativeAotLoaderHarness/CheatEngine.SDK.NativeAotLoaderHarness.csproj -c Release -o artifacts/nativeaot-loader-profile
artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLoaderHarness.exe --analyze artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLibraryProbe.dll
artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLoaderHarness.exe --load --acknowledge-process-resident-load
```

These commands do not start Cheat Engine or inspect its installation. They must not be retargeted at a production
plugin or an arbitrary DLL: loading executes the target's loader initialization even when no export is called.

## Consequences

- Existing managed packaging, the one-plugin live fixture, and the PR #56 coexistence fixture remain the baseline and
  retain their separate evidence classifications.
- The standalone executable probe remains a publication gate for the shipping graph; it is not converted into a
  library or made responsible for loader behavior.
- A future supported native route needs fresh exact-host evidence covering the host executable/version/hash,
  architecture, plugin path/dependencies, discovery, export ABI, activation, process-resident ownership, disable and
  removal behavior. It cannot use an unload assertion as an acceptance criterion for a NativeAOT library.
- **Proposal only:** a future SDK-owned native adapter plus an explicitly process-resident core could be investigated
  if an exact host profile needs a stable native boundary. This ADR does not design, implement, expose, or qualify
  that adapter/core. In particular, Client must not own a parallel bootstrap.

## Rejected alternatives

- Treating the current NativeAOT executable publish as CE plugin support: it exercises neither discovery nor the
  CE loader lifecycle.
- Loading a NativeAOT DLL in the current test process and balancing it with `NativeLibrary.Free`: Microsoft documents
  that unload as unsupported.
- Loading a NativeAOT DLL into Cheat Engine as an experiment: the pinned source contains a removal path using
  `FreeLibrary`, so the risk is outside the bounded test profile and no such run is authorized.
- Changing the managed package or Client composition to accommodate an unqualified native loader profile.
