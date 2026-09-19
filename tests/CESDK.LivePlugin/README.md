# CESDK.LivePlugin

A real x64 Cheat Engine plugin, built with the same components a package consumer gets, that you load into Cheat Engine
by hand.

## Objective

Load one plugin into Cheat Engine 7.7 and watch the whole chain work. It covers the generated entry point, the runtime
start, the lifecycle, the Lua functions, a typed `readInteger` call and the log output.

## Why it exists

Unit tests cannot start Cheat Engine. This project is the end-to-end check of what only a live host shows, and the
worked example behind the root [README](../../README.md).

## How it works

| File                 | Content                                                                                                                                                                  |
|----------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CesdkLivePlugin.cs` | The `[CheatEnginePlugin("CESDK Live Plugin")]` class. `OnEnable` logs the host context, registers the Lua functions and reads one integer. `OnDisable` unregisters them. |
| `LiveFunctions.cs`   | `[LuaFunction("cesdk_live_ping")]` returns 1, 2, 3 and so on. `[LuaFunction("cesdk_live_status")]` returns a status string.                                              |
| `MemoryBindings.cs`  | A `[LuaGlobal("readInteger")]` partial method: a typed call into a Cheat Engine Lua function.                                                                            |

The project references the six libraries directly, `CESDK.Analyzers`, `CESDK.Analyzers.CodeFixes` and the `EntryPoint`
and `LuaBindings` generators. That is the component set a package consumer gets. `EnableDynamicLoading` makes the build
write the `.deps.json` and `.runtimeconfig.json` a loaded component needs. `AllowUnsafeBlocks` is required because the
`LuaBindings` generator emits nothing without it (`CESDK2001`). The project sets both itself, where a package consumer
gets them from `CESDK.props`.

The root namespace is `LivePlugin`, never `CESDK.*`: in a plugin assembly the simple name `CESDK` binds to the generated
`CESDK.CESDK` (`CESDK0004`). Generated sources land in `artifacts/obj/CESDK.LivePlugin/generated/<Configuration>/`.

## Promise

- The analyzers and the `EntryPoint` and `LuaBindings` generators run on this project as on a package consumer's plugin,
  and `EngineApi` does not (`CESDK.LivePlugin.csproj`).
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
| Platform     | Windows x64 with Cheat Engine 7.7 and Sysinternals [DebugView](https://learn.microsoft.com/sysinternals/downloads/debugview)                                                                                                                                                                                   |
| SDK          | .NET SDK 10.0.401 or later, the version `global.json` pins                                                                                                                                                                                                                                                     |
| Runtimes     | The x64 .NET 10 runtimes: `dotnet --list-runtimes` lists 10.x of `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and `Microsoft.AspNetCore.App`. A missing runtime stops Cheat Engine from starting the plugin even with the runtime configuration change.                                                     |
| Runtime request | Cheat Engine 7.7 requests .NET 9 in `ce.runtimeconfig.json` (`net9.0`, `9.0.0`, `latestMinor`). To require .NET 10, set `runtimeOptions.tfm` to `net10.0`, the `version` of every framework request (`framework` or `frameworks`) to `10.0.0` for `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App`, and `Microsoft.AspNetCore.App`, and `runtimeOptions.rollForward` to `LatestMinor`. Set any framework-level `rollForward` to `LatestMinor` too. This stays on .NET 10 even with .NET 9 or 11 installed. |

1. Build the plugin with `dotnet build tests/CESDK.LivePlugin/CESDK.LivePlugin.csproj -c Release`. The output folder is
   `artifacts/bin/CESDK.LivePlugin/release/`. Keep the whole folder together: the libraries and
   `cesdk-lua-bridge.dll` sit beside `CESDK.LivePlugin.dll`.
2. Start DebugView, turn on Capture > Capture Global Win32 and add a filter for `CESDK`.
3. Start Cheat Engine after applying the runtime configuration above. To repeat its .NET 10 `LatestMinor` policy from a
   shell in the Cheat Engine folder, run:

   ```powershell
   $env:DOTNET_ROLL_FORWARD = "LatestMinor"
   .\cheatengine-x86_64.exe
   ```

4. Open Edit > Settings > Plugins, choose Add new, select `CESDK.LivePlugin.dll` and tick it. You should see
   `CESDK Live Plugin` in the list.
5. Read DebugView. You should see the entries listed below, each prefixed `[CESDK.Hosting] Information:`.
6. In Cheat Engine's Lua Engine window, run `print(cesdk_live_ping())` twice and then `print(cesdk_live_status())`. You
   should see `1`, `2` and `CESDK Live Plugin enabled; cesdk_live_ping has been called 2 time(s).`
7. Untick the plugin. You should see `CESDK Live Plugin: UnregisterLuaFunctions -> LUA_OK.` and `Plugin <id> disabled.`,
   and `cesdk_live_ping` is `nil`. Tick it again and `OnEnable #2 (...)` follows.

| Entry                                                                        | Content                                                                                                                                                                                                                                                                                                                                                               |
|------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CESDK Live Plugin: OnEnable #1 (...)`                                       | The enable count of this plugin instance, then a `PluginContext:` line and a `PluginHost:` line. They print `PluginId`, `Epoch`, `MainThreadId`, `HasProcessMessages`, `HasCheckSynchronize` and `IsInitialized`. They also print `ReportedExportsSize`, `LastInitRecordSize` and `LastVersionRecordSize` next to the values the SDK expects: 48, 36 and at least 16. |
| `CESDK Live Plugin: RegisterLuaFunctions -> LUA_OK.`                         | The Lua functions are registered.                                                                                                                                                                                                                                                                                                                                     |
| `CESDK Live Plugin: readInteger(00400000) -> ok=<True or False>, value=<n>.` | The address is a placeholder, and `ok=False` means the read failed. To see a value, set a readable address of an attached process in `CesdkLivePlugin.cs`.                                                                                                                                                                                                            |
| `Plugin <id> enabled (epoch <n>).`                                           | The host confirms the enable.                                                                                                                                                                                                                                                                                                                                         |

If DebugView stays empty, confirm that capture is on and the plugin is ticked, that `ce.runtimeconfig.json` requests
.NET 10 as described above, and that `dotnet --list-runtimes` lists the required .NET 10 runtimes. A
`[CESDK.Hosting] Error:` entry names the step that failed, because the host logs every failed enable. If Cheat Engine
refuses the DLL, check that `CESDK.EntryPoint.g.cs` exists under `artifacts/obj/CESDK.LivePlugin/generated/Release/`.
