# CheatEngine.SDK.LivePlugin.Coexistence

An opt-in, manual fixture for the exact-host portion of qualification scenarios Q09 and Q10 (see
[qualification](../../docs/qualification/README.md)).
It builds two distinct plugin assemblies, `PluginA` and `PluginB`, and records the identities that Cheat Engine actually
loads. It is not a unit test, it is not part of ordinary CI, and this repository contains **no executed result** for it.

## Why it exists

`PluginHost` has static lifecycle state. Static state belongs to the loaded `CheatEngine.SDK.Hosting` assembly instance;
it is not inherently scoped to a plugin DLL, a Client service provider, or a Cheat Engine process. Whether two plugin
DLLs receive separate SDK assembly instances is a property of the exact Cheat Engine loader/runtime profile. It must be
measured, not assumed from `AssemblyLoadContext` documentation or from a one-plugin fixture.

Each plugin has a different assembly identity, display name and Lua export names. On enable it emits both its own and
the `CheatEngine.SDK.Hosting` assembly identity, their module-version IDs, each runtime `AssemblyLoadContext`
name/collectibility, whether those two contexts are equal, and its CE plugin ID/epoch. It does not select a target
process, mutate memory, install hooks, or create a loader isolation model.

## What it can and cannot establish

| Observation                                    | Establishes                                                                           | Does not establish                                                               |
|------------------------------------------------|---------------------------------------------------------------------------------------|----------------------------------------------------------------------------------|
| A and B enable and answer separate Lua globals | The recorded host accepted both exact output directories for that run                 | A general CE version/loader guarantee                                            |
| The identity lines                             | The actual plugin/Hosting assembly and runtime load-context relationship for that run | That all static state is safely isolated; Lua and CE globals can still be shared |
| Disable A while B remains callable             | The narrow A/B global-registration and lifecycle observation                          | Callback, worker-dispatch, target-provenance, or retained-owner safety           |

The fixture intentionally uses the current source graph for both plugins. A side-by-side SDK-version run is separate:
obtain two qualified package/output tuples, keep each complete dependency set in its own directory, record their
package IDs, versions and SHA-256 hashes, then repeat this protocol. Do not mix DLLs from two outputs in one directory
or infer the selected SDK version from a file name. No versioned package tuple is qualified by this fixture or README.

## Prerequisites and evidence record

Use only the controlled, x64 Cheat Engine 7.7 host documented by
[the one-plugin live fixture](../CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine). Before loading either
DLL, record:

- Cheat Engine executable version, x64 architecture and SHA-256;
- .NET/`hostfxr` runtime policy and the exact plugin directories;
- the source commit or each package ID/version/content hash, plus the SHA-256 of every DLL loaded from both directories;
- the timestamp, operator and complete DebugView transcript; and
- the Plugin A/B identity lines, every Lua command result, and any loader/enable failure.

Leaving any field unknown means the result is an unqualified manual observation. Qualification scenarios Q09 and Q10
remain not executed, as do the related scenarios Q19 and Q30.

## Build and run

Build both output directories independently. Do not copy either result into the other directory.

```powershell
dotnet build tests/CheatEngine.SDK.LivePlugin.Coexistence/PluginA/CheatEngine.SDK.LivePlugin.Coexistence.PluginA.csproj -c Release
dotnet build tests/CheatEngine.SDK.LivePlugin.Coexistence/PluginB/CheatEngine.SDK.LivePlugin.Coexistence.PluginB.csproj -c Release
```

The output directories are:

```text
artifacts/bin/CheatEngine.SDK.LivePlugin.Coexistence.PluginA/release/
artifacts/bin/CheatEngine.SDK.LivePlugin.Coexistence.PluginB/release/
```

Keep each output directory intact; `cheatengine-sdk-lua-bridge.dll`, the managed SDK assemblies, `.deps.json`, and
`.runtimeconfig.json` remain adjacent to their respective plugin. Start DebugView with global Win32 capture and a
`CheatEngine.SDK` filter, then use the controlled host's **Edit > Settings > Plugins** UI to add both DLLs. This test
does not change the installed Cheat Engine runtime configuration.

1. Enable A, then enable B. Record both `CheatEngine.SDK coexistence <A|B>` identity lines and registration results.
2. In Cheat Engine's Lua Engine, record:

   ```lua
   print(cheatengine_sdk_coexistence_a_identity())
   print(cheatengine_sdk_coexistence_b_identity())
   print(cheatengine_sdk_coexistence_a_ping())
   print(cheatengine_sdk_coexistence_b_ping())
   ```

3. Disable A. Record that A's two globals are `nil`; invoke B's identity and ping again and record the outcome.
4. Re-enable A, then disable B and finally A. Record every lifecycle result and whether either disable was refused or
   left the host in `Disabling`.

Stop and record failure if a plugin cannot load, cannot enable, a global remains after its own plugin is disabled, or
the surviving plugin cannot answer. Do not repair a failed run by manually assigning Lua globals: that would erase the
behaviour the fixture is meant to observe.

## Loader and AOT limits

This fixture observes a managed plugin route only. .NET permits a collectible `AssemblyLoadContext` to be unloaded
cooperatively only after relevant threads and strong references are gone; it does not say what load context Cheat
Engine uses for a plugin.
See [AssemblyLoadContext unloadability](https://learn.microsoft.com/dotnet/standard/assembly/unloadability).

Native AOT is a separate deployment profile. A standalone AOT publish/probe is publication evidence, not proof that
Cheat Engine can load, disable and remove a native plugin. Microsoft documents that unloading Native AOT libraries with
`FreeLibrary`/`dlclose` is unsupported; a native CE route needs a qualified residency design and its own live evidence.
See [Native AOT libraries](https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries).

## Scope deliberately left to follow-up fixtures

- Q19: first-thread Lua acquisition, refusal and cross-plugin worker/main-thread concurrency;
- Q30: target switch and retained allocation/patch ownership; and
- a real side-by-side package test, after the package tuple and loader profile are identified.

Those scenarios need their production owners and exact host facts. This fixture must not be used to advertise them as
qualified.
