# CESDK0006: Method exports a classic Cheat Engine native plugin entry point

|                    |                                                |
|--------------------|------------------------------------------------|
| Category           | `CheatEngine.SDK.Plugin`                       |
| Default severity   | Warning                                        |
| Enabled by default | Yes                                            |
| Code fix           | No                                             |
| Reported           | While typing and in build output               |

## Cause

A method or local function of a project that references CheatEngine.SDK carries `[UnmanagedCallersOnly]` with a
constant `EntryPoint` that starts with `CEPlugin_`, for example:

```csharp
[UnmanagedCallersOnly(EntryPoint = "CEPlugin_GetVersion", CallConvs = [typeof(CallConvStdcall)])]
private static int GetVersion(nint version, int size) { ... }

[UnmanagedCallersOnly(EntryPoint = NativeExportNames.InitializePlugin)]
private static int Initialize(nint exports, int pluginId) { ... }
```

## Why

`CEPlugin_GetVersion`, `CEPlugin_InitializePlugin` and `CEPlugin_DisablePlugin` are the three exports by which Cheat
Engine recognises a **classic native** plugin DLL. A .NET assembly gets such exports only when it is published with
NativeAOT: `UnmanagedCallersOnly` methods with an `EntryPoint` of the published assembly become native exports
([Native AOT interop, native exports](https://learn.microsoft.com/dotnet/core/deploying/native-aot/interop#native-exports)).
The CheatEngine.SDK package can never add them for you; only your own source can.

That route is not a supported CheatEngine.SDK profile:

- Cheat Engine unloads a plugin with `FreeLibrary`, and .NET does not support unloading a NativeAOT library
  ([Native AOT libraries](https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries)). No residence model
  exists that would make such a plugin removable (audit F02, scenario Q42 recorded `NotApplicable`).
- CheatEngine.SDK plugins are framework-dependent assemblies loaded through the managed hostfxr profile, with the
  generated `CESDK.CESDK.CEPluginInitialize` entry point and the 48-byte managed exports table. The classic 159-slot
  table a `CEPlugin_InitializePlugin` export receives has no managed route and no SDK facade.
- Replacing the managed bootstrap by three native exports is not a complete solution: it changes the load profile, the
  table the plugin receives and the unload contract all at once.

See the [NativeAOT plugin profile](../../libs/CheatEngine.SDK.Abi/README.md) page for the profile table.

## What is checked

- Methods and local functions with `[UnmanagedCallersOnly]` from `System.Runtime.InteropServices`, in a compilation that
  references CheatEngine.SDK (the plugin attribute or the plugin base class resolves).
- The named argument `EntryPoint`, whose value is always a compile-time constant (a literal or a constant such as
  `NativeExportNames.GetVersion`). It is reported when the value starts with `CEPlugin_` (ordinal, case-sensitive). One
  diagnostic per attribute, located on the attribute.
- Not flagged: other entry-point names, `[UnmanagedCallersOnly]` without `EntryPoint`, and the historical unprefixed
  names (`GetVersion`, `InitializePlugin`, `DisablePlugin`) that the host also tries: they are ordinary words and would
  produce false positives. This is a documented limitation.
- Generated code is not analysed.

## How to fix

Remove the `CEPlugin_*` exports and write a managed plugin: a class marked `[CheatEnginePlugin]` deriving from
`CheatEnginePlugin`, loaded through the generated entry point. Keep `PublishAot` off for the plugin (see
[CESDK9102](CESDK9102.md)).

## When to suppress

Only when the assembly is deliberately not a CheatEngine.SDK plugin, for example a research harness that inspects its
own exports without ever being loaded by Cheat Engine, and you accept that nothing in CheatEngine.SDK supports that
route.
