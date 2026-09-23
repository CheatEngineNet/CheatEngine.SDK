# Changelog

All notable changes to CheatEngine.SDK are documented in this file. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/).

Versions 0.1.0 to 0.2.1 were published under the former `CESDK` package ID before this file existed. Version 1.0.0 is a
rewrite published as `CheatEngine.SDK`; it is neither source nor binary compatible with them. Read the breaking changes
under [1.0.0](#100---2026-09-20) before upgrading.

## [Unreleased]

The next release line is 2.0.0. Every pack is validated against the published 1.0.0 package, and each intentional break
is declared in `src/CheatEngine.SDK/CompatibilitySuppressions.xml` and marked **Breaking** below.

### Added

- The package embeds an SPDX 2.2 software bill of materials at `_manifest/spdx_2.2/manifest.spdx.json`, and its nuspec
  keeps the repository URL and commit.
- A security policy with private vulnerability reporting, and a compatibility issue form that captures the exact
  package, bridge, Cheat Engine and runtime tuple.
- `LuaOptional<T>` lets a generated Lua or Engine API binding distinguish an omitted trailing argument (`LUA_TNONE`)
  from an explicit Lua `nil`, and lets a result read the factual count Lua returned instead of assuming the declared
  arity; the Engine API grammar gains matching `opt:`, `opt-result:` and `rest:` declarations, and every curated spec
  must now declare the `contract: ce77` header.
- `AobScanner.TryScanWithinBounds` runs an exhaustive or a deadline-bounded, cooperatively terminable AOB scan over an
  explicit target range, and `TryFindFirstFoundWithinBounds` stops at the first match; both accept byte-array
  patterns and explicit range bounds. The deadline/termination overloads and the first-found scan are marked
  `[Experimental]` (`CESDK5010`, `CESDK5011`, build errors unless acknowledged) until their exact-host evidence lands.
- `TargetReleaseStatus.RefusedRuntimeChanged` reports that a target-bound owner was consumed without any Cheat Engine
  call because the Lua runtime identity that created it (attach epoch or state generation) is no longer current.
- `RuntimeInfo.TryDeriveTargetArchitecture` derives the target CPU architecture from Cheat Engine's own instruction-
  family and bitness facts.
- Diagnostics `CESDK9102` (a direct plugin-library consumer sets `PublishAot=true`) and `CESDK0006` (a method exports
  a classic `CEPlugin_*` native entry point) flag the two plugin shapes CheatEngine.SDK does not support; `CESDK1020`
  flags a `PointerSize` built from the plugin process width instead of a Cheat Engine observation.

### Changed

- Untagged builds are versioned `2.0.0-alpha.0.N` with `AssemblyVersion` 2.0.0.0, so the `[GeneratedCode]` attribute
  that the generators emit carries 2.0.0.0.
- Building this repository requires .NET SDK 10.0.401 exactly (`global.json` `rollForward: disable`), and every
  project restores against a committed NuGet lock file. Plugin projects that consume the package are not affected.
- The packaged native Lua protection bridge is built by CI with a pinned MSVC toolset (14.44) and Windows SDK
  (10.0.26100.0), and a second build and a build from a copy in another directory must produce the same bytes; the
  toolchain facts are published with every CI run.
- `native/cheatengine-sdk-lua-bridge/bridge-audit-manifest.json` now describes the committed bridge DLL and its sources,
  and a test checks it against the committed file.
- Releases are created as drafts with the SPDX 2.2 SBOM, provenance and SBOM attestations, `SHA256SUMS`, sigstore
  bundles and a release tuple, and are verified on nuget.org before they are published; the packaging tests run
  against the exact package that is published.
- Documentation no longer links to the retired `documentations/` tree, and no narrative documentation is recreated
  under a repository `docs/` folder; a repository test rejects dead links, local paths and relative links in the
  packed README.
- The CE 7.7 live probe is now compiled with the solution.
- A target that reports the x86 instruction family together with the 64-bit flag — how Cheat Engine reports every x64
  target — now resolves to the `X64` architecture instead of being refused as a contradictory profile.
- Integer, address and pointer marshallers refuse a Lua float at or above 2^53 instead of narrowing it silently.
- A zero-match `AOBScan` is modeled as the factual `AobScanStatus.NoResult`, matching how Cheat Engine 7.7 reports it
  (no value, read as `nil`); it is never presented as a "no matches" classification.
- `PointerSize.FromArchitecture` is obsolete (`CESDK7001`); read the target bitness or configured pointer size from
  Cheat Engine instead.
- **Breaking, not reported by ApiCompat:** every SDK-owned outcome enum added since 1.0.0 now starts at `Unknown = 0`
  instead of reading a default value as success: `LuaOperationStatusKind`, `LuaGlobalPushStatus`,
  `InstructionOperationStatus`, `ProcessOperationStatusKind` and `AobScanStatus`. None of these enums existed in the
  published 1.0.0 package, so ApiCompat does not see the renumbering.
- **Breaking:** the `Callback` fields of `AddressListPluginInit` and `DisassemblerContextPluginInit` changed from a
  typed function pointer to `void*`.
- **Breaking:** `MemoryAccessFailure.DestinationTooSmall`, `WriteFailed` and `InvalidResult` were renumbered from 4, 5
  and 6 to 5, 8 and 9.
- **Breaking, not reported by ApiCompat:** `LuaClassAttribute` and `LuaPropertyAttribute` no longer set
  `Inherited = false`, and `MemoryScanSession.Scanner` and `MemoryScanSession.Results` are now marked
  `[RequiresPluginEnabled]`, so calling them from plugin startup code reports `CESDK1001`.

### Removed

- **Breaking:** removed the obsolete host-symbol compatibility flag `UseHostSymbolTable` from `AddressResolutionOptions`
  (its constructor, property accessors and `Deconstruct` change); use `EngineInspection.ResolveHostAddress` for
  host-symbol resolution.

### Fixed

- A symbol registration whose release never reached Cheat Engine is no longer read as a successful release.
- An AOB result list is destroyed once, even when the call that would hand it to its caller cannot proceed.
- A scan is stopped before its session is released, a disposed session refuses further scan members, and a release
  requested from inside the session's own Cheat Engine wait is deferred instead of re-entering it; a bounded scan's
  session is released if staging its allocation fails.

## [1.0.0] - 2026-09-20

> [!WARNING]
> **Major breaking change: 1.0.0 is a new SDK, not an update of `CESDK` 0.2.1.** The new architecture replaces the
> target framework, the plugin model and the whole public API, so nothing written against 0.1.0 to 0.2.1 works the
> same way. There is no compatibility layer and no deprecation period. A plugin has to be rewritten against the new
> API, starting from the [quick start](README.md#quick-start). A plugin that stays on `CESDK` 0.2.1 keeps building as
> before.

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
