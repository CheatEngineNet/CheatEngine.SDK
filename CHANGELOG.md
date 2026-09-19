# Changelog

All notable changes to CheatEngine.SDK are documented in this file. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/).

Versions 0.1.0 to 0.2.1 were published before this file existed. Version 0.3.0 is a rewrite that is neither source nor
binary compatible with them. Read the breaking changes under [0.3.0](#030---2026-09-18) before upgrading.

The project was called CESDK until the rename described under [Unreleased](#unreleased). The entry for 0.3.0 keeps the
names that release shipped with.

## [Unreleased]

### Changed

- The project is renamed CheatEngine.SDK. It was called CESDK.
- The NuGet package id, the assemblies and the root namespace change from `CESDK` to `CheatEngine.SDK`, for example
  `CESDK.Hosting.Plugin` becomes `CheatEngine.SDK.Hosting.Plugin`. A plugin replaces its `CESDK` package reference with
  `CheatEngine.SDK` and updates its `using` directives.
- The MSBuild switch `CesdkGenerateEntryPoint` is now `CheatEngineSdkGenerateEntryPoint`.
- The native Lua bridge `cesdk-lua-bridge.dll` is now `cheatengine-sdk-lua-bridge.dll`, and its exports are prefixed
  `cheatengine_sdk_`.
- The environment variable `CESDK_LUA53_PATH`, which only the tests read, is now `CHEATENGINE_SDK_LUA53_PATH`.
- The engine API spec files `*.cesdk-api.txt` are now `*.cheatengine-sdk-api.txt`.
- The diagnostic categories `CESDK.Plugin`, `CESDK.Usage` and `CESDK.Generation` are now `CheatEngine.SDK.Plugin`,
  `CheatEngine.SDK.Usage` and `CheatEngine.SDK.Generation`.

The diagnostic IDs keep their `CESDK` prefix (`CESDK0001` and so on), and so do the names of their pages in
`analyzers/docs`. Cheat Engine still looks for the type `CESDK.CESDK` in a plugin assembly, so the entry point the SDK
generates keeps that name, and `CESDK0004` still warns about a plugin namespace under `CESDK`.

## [0.3.0] - 2026-09-18

> [!WARNING]
> **Major breaking change: 0.3.0 is a new SDK, not an update of 0.2.1.** The new architecture replaces the target
> framework, the plugin model and the whole public API, so nothing written against 0.1.0 to 0.2.1 works the same way.
> There is no compatibility layer and no deprecation period. A plugin has to be rewritten against the new API,
> starting from the [quick start](README.md#quick-start). A plugin that stays on 0.2.1 keeps building as before.

### Added

- One NuGet package that embeds the CESDK libraries, the entry point and Lua binding generators, and the analyzers.
- A generated `CESDK.CESDK.CEPluginInitialize` entry point for the class marked `[CheatEnginePlugin]`, and the plugin
  lifecycle behind it: `CheatEnginePlugin`, `PluginContext`, main thread access, and logging.
- `[LuaFunction]` and `[LuaGlobal]` bindings on top of a managed Lua 5.3 layer that binds to the Lua library Cheat
  Engine already loaded.
- Eight analyzer rules from `CESDK0001` to `CESDK2004`, with code fixes for `CESDK0001` and `CESDK1004`.
- The Cheat Engine object model in `CESDK.Engine`: borrowed and owned handles, `Address`, enums, and generated memory
  read and write wrappers.

### Changed

- The SDK targets .NET 10 and C# 14 on Windows x64, replacing the `netstandard2.0` wrapper published as 0.1.0 to 0.2.1.
- Consumers need .NET SDK 10.0.401 or later. The analyzers and generators are built against Roslyn 5.9, and older
  compilers report `CS9057` and skip them.
- The plugin base class moved to `CESDK.Hosting.Plugin`. The plugin name comes from `[CheatEnginePlugin("Name")]`
  instead of an overridden `Name` property, and `OnEnable` and `OnDisable` are abstract and run on the main thread.
- The SDK is consumed as the `CESDK` package only. Compiling the CESDK sources into the plugin project no longer
  applies, and the entry point is generated into the plugin assembly.
- Cheat Engine is no longer reached through one facade per feature. A plugin declares the Cheat Engine Lua functions
  it needs with `[LuaGlobal]` and exposes its own with `[LuaFunction]`, or works with the `CESDK.Engine` types. The
  generated memory wrappers cover `readInteger`, `writeInteger`, `readQword` and `writeQword`.
- Failures are no longer wrapped in `CesdkException` subclasses. Try members return `bool` or `LuaStatus`, and
  `LuaException` reports a failed Lua call.
- Work that must run on the main thread goes through `MainThread` in `CESDK.Hosting.Threading`, and logging goes
  through the host.

### Removed

- The static facades of the `CESDK.Classes` namespace, among them `Process`, `MemoryAccess`, `AobScanner`, `MemScan`,
  `Debugger`, `Dbvm`, `AddressList`, `StructureManager` and `Injection`.
- `CESDK.Synchronize`, `LuaExecutor`, `LuaLogger`, `LuaNative`, `LuaUtils` and `PluginLogger`.
- The `CesdkException` hierarchy.
- The NLog dependency.

[Unreleased]: https://github.com/CheatEngineNet/CheatEngine.SDK/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/CheatEngineNet/CheatEngine.SDK/releases/tag/v0.3.0
