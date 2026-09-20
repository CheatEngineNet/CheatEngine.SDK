# CheatEngine.SDK.LivePlugin

A real x64 Cheat Engine plugin, built with the same components a package consumer gets, that you load into Cheat Engine
by hand.

## Objective

Load one plugin into Cheat Engine 7.7 and watch the whole chain work. It covers the generated entry point, the runtime
start, the lifecycle, the Lua functions, a typed `readInteger` call and the log output.

## Why it exists

Unit tests cannot start Cheat Engine. This project is a manual observation harness for the portions that only a live
host can show, and the worked example behind the root [README](../../README.md). Its output is evidence only after an
operator records the exact host binary, architecture, runtime policy and transcript; it is not an automated proof of
the unresolved CE 7.7 contracts.

## How it works

| File                          | Content                                                                                                                                                                            |
|-------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEngineSdkLivePlugin.cs` | The `[CheatEnginePlugin("CheatEngine.SDK Live Plugin")]` class. `OnEnable` logs the host context, registers the Lua functions and reads one integer. `OnDisable` unregisters them. |
| `LiveFunctions.cs`            | `[LuaFunction("cheatengine_sdk_live_ping")]` returns 1, 2, 3 and so on. `[LuaFunction("cheatengine_sdk_live_status")]` returns a status string.                                    |
| `MemoryBindings.cs`           | A `[LuaGlobal("readInteger")]` partial method: a typed call into a Cheat Engine Lua function.                                                                                      |

The project references the six libraries directly, `CheatEngine.SDK.Analyzers`, `CheatEngine.SDK.Analyzers.CodeFixes` and the `EntryPoint`
and `LuaBindings` generators. That is the component set a package consumer gets. `EnableDynamicLoading` makes the build
write the `.deps.json` and `.runtimeconfig.json` a loaded component needs. `AllowUnsafeBlocks` is required because the
`LuaBindings` generator emits nothing without it (`CESDK2001`). The project sets both itself, where a package consumer
gets them from `CheatEngine.SDK.props`.

The root namespace is `LivePlugin`, never `CESDK.*` (`CESDK0004`): Cheat Engine requires the generated type `CESDK.CESDK`
in the plugin assembly, and inside the namespace `CESDK` the simple name `CESDK` binds to that type. Generated sources
land in `artifacts/obj/CheatEngine.SDK.LivePlugin/generated/<Configuration>/`.

## Promise

- The analyzers and the `EntryPoint` and `LuaBindings` generators run on this project as on a package consumer's plugin,
  and `EngineApi` does not (`CheatEngine.SDK.LivePlugin.csproj`).
- A failure while enabling is logged and never thrown into Cheat Engine, so DebugView shows the reason
  (`EnablePluginTests`).
- `OnDisable` calls the generated `UnregisterLuaFunctions`, which sets each registered global to `nil`
  (`LuaFunctionEndToEndTests`).
- Enabling again reuses the same plugin instance (`DisablePluginTests`).
- It is not a test project and never packs: `eng/Tests.props` turns only `*.Tests` projects into test projects, and
  `IsPackable` is `false`.

## Run it in Cheat Engine

Prerequisites:

| Need         | Detail                                                                                                                                                                                                                                                                                                         |
|--------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Platform     | Windows x64 with the pinned CE `7.7.0.10621` host (verify SHA-256 `9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D`) and Sysinternals [DebugView](https://learn.microsoft.com/sysinternals/downloads/debugview)                                                                    |
| SDK          | .NET SDK 10.0.401 or later, the version `global.json` pins                                                                                                                                                                                                                                                     |
| Runtime policy | The inspected CE binary uses the `nethost`/`hostfxr` route. The workspace's .NET 10 `ce.runtimeconfig.json` is a local modification, not an installer baseline. Preserve the controlled host's existing policy and record it with the transcript; do not treat this guide as permission to edit an installed CE runtime configuration. Microsoft documents `nethost`/`hostfxr` for framework-dependent components. |

1. Build the plugin with `dotnet build tests/CheatEngine.SDK.LivePlugin/CheatEngine.SDK.LivePlugin.csproj -c Release`. The output folder is
   `artifacts/bin/CheatEngine.SDK.LivePlugin/release/`. Keep the whole folder together: the libraries and
   `cheatengine-sdk-lua-bridge.dll` sit beside `CheatEngine.SDK.LivePlugin.dll`.
2. Start DebugView, turn on Capture > Capture Global Win32 and add a filter for `CheatEngine.SDK`.
3. Start the previously verified controlled Cheat Engine host under its recorded runtime policy. Do not change the
   installed host configuration merely to run this example.

4. Open Edit > Settings > Plugins, choose Add new, select `CheatEngine.SDK.LivePlugin.dll` and tick it. You should see
   `CheatEngine.SDK Live Plugin` in the list.
5. Read DebugView. You should see the entries listed below, each prefixed `[CheatEngine.SDK.Hosting] Information:`.
6. In Cheat Engine's Lua Engine window, run `print(cheatengine_sdk_live_ping())` twice and then `print(cheatengine_sdk_live_status())`. You
   should see `1`, `2` and `CheatEngine.SDK Live Plugin enabled; cheatengine_sdk_live_ping has been called 2 time(s).`
7. Untick the plugin. You should see `CheatEngine.SDK Live Plugin: UnregisterLuaFunctions -> LUA_OK.` and `Plugin <id> disabled.`,
   and `cheatengine_sdk_live_ping` is `nil`. Tick it again and `OnEnable #2 (...)` follows.

| Entry                                                                                  | Content                                                                                                                                                                                                                                                                                                                                                               |
|----------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEngine.SDK Live Plugin: OnEnable #1 (...)`                                       | The enable count of this plugin instance, then a `PluginContext:` line and a `PluginHost:` line. They print `PluginId`, `Epoch`, `MainThreadId`, `HasProcessMessages`, `HasCheckSynchronize` and `IsInitialized`. `ReportedExportsSize` is expected to be 48 on x64; `LastInitRecordArgument` is only the raw second integer from `CEPluginInitialize` and has no assigned size/version meaning until a CE 7.7 live probe records it. `LastVersionRecordSize` is logged separately. |
| `CheatEngine.SDK Live Plugin: RegisterLuaFunctions -> LUA_OK.`                         | The Lua functions are registered.                                                                                                                                                                                                                                                                                                                                     |
| `CheatEngine.SDK Live Plugin: readInteger(00400000) -> ok=<True or False>, value=<n>.` | The address is a placeholder, and `ok=False` means the read failed. To see a value, set a readable address of an attached process in `CheatEngineSdkLivePlugin.cs`.                                                                                                                                                                                                   |
| `Plugin <id> enabled (epoch <n>).`                                                     | The host confirms the enable.                                                                                                                                                                                                                                                                                                                                         |

If DebugView stays empty, confirm that capture is on, the plugin is ticked, and the host hash and runtime policy match
the recorded controlled setup. A
`[CheatEngine.SDK.Hosting] Error:` entry names the step that failed, because the host logs every failed enable. If Cheat Engine
refuses the DLL, check that `CheatEngine.SDK.EntryPoint.g.cs` exists under `artifacts/obj/CheatEngine.SDK.LivePlugin/generated/Release/`.
