# Changelog

All notable changes to CheatEngine.SDK are documented in this file. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/).

Versions 0.1.0 to 0.2.1 were published under the former `CESDK` package ID before this file existed. Version 1.0.0 is a
rewrite published as `CheatEngine.SDK`; it is neither source nor binary compatible with them. Read the breaking changes
under [1.0.0](#100---2026-09-20) before upgrading.

## [Unreleased]

- `AddressResolutionOptions.UseHostSymbolTable` is retained only for source and binary compatibility and is rejected by
  `EngineInspection.ResolveAddress`; use `EngineInspection.ResolveHostAddress` for host-symbol resolution.

## [1.0.0] - 2026-09-20

> [!WARNING]
> **Major breaking change: 1.0.0 is a new SDK, not an update of `CESDK` 0.2.1.** The new architecture replaces the target
> framework, the plugin model and the whole public API, so nothing written against 0.1.0 to 0.2.1 works the same way.
> There is no compatibility layer and no deprecation period. A plugin has to be rewritten against the new API,
> starting from the [quick start](README.md#quick-start). A plugin that stays on `CESDK` 0.2.1 keeps building as before.

### Added

- One `CheatEngine.SDK` NuGet package that embeds the SDK libraries, the entry point and Lua binding generators, and
  the analyzers.
- A generated `CESDK.CESDK.CEPluginInitialize` entry point for the class marked `[CheatEnginePlugin]`, and the plugin
  lifecycle behind it: `CheatEnginePlugin`, `PluginContext`, main thread access, and logging.
- `[LuaFunction]` and `[LuaGlobal]` bindings on top of a managed Lua 5.3 layer that binds to the Lua library Cheat
  Engine already loaded.
- Eight analyzer rules from `CESDK0001` to `CESDK2004`, with code fixes for `CESDK0001` and `CESDK1004`.
- The Cheat Engine object model in `CheatEngine.SDK.Engine`: borrowed and owned handles, `Address`, enums, and
  generated memory read and write wrappers.

### Changed

- The SDK targets .NET 10 and C# 14 on Windows x64, replacing the `netstandard2.0` wrapper published as 0.1.0 to 0.2.1.
- Consumers need .NET SDK 10.0.401 or later. The analyzers and generators are built against Roslyn 5.9, and older
  compilers report `CS9057` and skip them.
- The plugin base class moved to `CheatEngine.SDK.Hosting.Plugin`. The plugin name comes from
  `[CheatEnginePlugin("Name")]`
  instead of an overridden `Name` property, and `OnEnable` and `OnDisable` are abstract and run on the main thread.
- The SDK is consumed as the `CheatEngine.SDK` package only. Compiling SDK sources into the plugin project no longer
  applies, and the entry point is generated into the plugin assembly.
- Cheat Engine is no longer reached through one facade per feature. A plugin declares the Cheat Engine Lua functions
  it needs with `[LuaGlobal]` and exposes its own with `[LuaFunction]`, or works with the `CheatEngine.SDK.Engine`
  types. The generated memory wrappers cover `readInteger`, `writeInteger`, `readQword` and `writeQword`.
- Failures are no longer wrapped in the project-specific exception hierarchy from earlier releases. Try members
  return `bool` or `LuaStatus`, and `LuaException` reports a failed Lua call.
- Work that must run on the main thread goes through `MainThread` in `CheatEngine.SDK.Hosting.Threading`, and logging
  goes through the host.
### Removed

- The earlier static facades, among them `Process`, `MemoryAccess`, `AobScanner`, `MemScan`, `Debugger`, `Dbvm`,
  `AddressList`, `StructureManager` and `Injection`.
- The earlier `Synchronize`, `LuaExecutor`, `LuaLogger`, `LuaNative`, `LuaUtils` and `PluginLogger` helpers.
- The earlier project-specific exception hierarchy.
- The NLog dependency.

[Unreleased]: https://github.com/CheatEngineNet/CheatEngine.SDK/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/CheatEngineNet/CheatEngine.SDK/releases/tag/v1.0.0
