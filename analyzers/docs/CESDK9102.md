# CESDK9102: A CheatEngine.SDK plugin library sets PublishAot

|                    |                                                                          |
|--------------------|--------------------------------------------------------------------------|
| Kind               | MSBuild warning of the packaged build target (not a Roslyn analyzer)     |
| Reported by        | Target `CheatEngineSdkWarnNativeAotPluginProfile`, `build/CheatEngine.SDK.targets` |
| Default severity   | Warning                                                                  |
| Applies to         | Projects with a **direct** `PackageReference` to CheatEngine.SDK         |
| Reported           | Before `BeforeBuild`, on every build (not only on publish)               |

## Cause

A project that references the `CheatEngine.SDK` package directly sets `PublishAot=true` and is not an executable
(`OutputType` is neither `Exe` nor `WinExe`), with or without `NativeLib=Shared` or `NativeLib=Static`:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <NativeLib>Shared</NativeLib>
</PropertyGroup>
```

## Why

A library published with NativeAOT is a native DLL. Cheat Engine would load it through its classic native plugin path,
and removes a plugin with `FreeLibrary`; .NET does not support unloading a NativeAOT library
([Native AOT libraries](https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries)). No residence model
exists for such a plugin in CheatEngine.SDK 2.0, so that profile is not supported (audit F02).

CheatEngine.SDK plugins load through the **managed hostfxr profile**: a framework-dependent plugin folder (the plugin
assembly, the SDK assemblies, `*.deps.json`, `*.runtimeconfig.json` and the Lua protection bridge) that Cheat Engine's
.NET host loads and calls through the generated `CESDK.CESDK.CEPluginInitialize` entry point. `PublishAot` changes none
of that for the better and produces an artefact Cheat Engine cannot unload.

"The shipping libraries are AOT-compatible" is a statement about trimming and AOT analysis of the libraries, verified by
a probe executable; it is not a promise that a NativeAOT plugin DLL can be loaded or removed. See the
[NativeAOT plugin profile](../../libs/CheatEngine.SDK.Abi/README.md) page.

## What is checked

- `'$(PublishAot)' == 'true'` and `'$(OutputType)'` is not `Exe` or `WinExe` (case-insensitive, like every MSBuild
  condition). An executable that publishes with NativeAOT (a tool, a probe) is a different artefact and is not warned.
- Only direct package consumers: the target is a `build/` asset, which NuGet imports for a direct reference and never
  for an indirect (transitive) one.
- The message names the project, the `NativeLib` value, and links here.

## How to fix

Remove `PublishAot` (and `NativeLib`) from the plugin project. Build or publish the plugin as a normal framework-dependent
class library.

## When to suppress

When the library is not a Cheat Engine plugin but still references CheatEngine.SDK directly, and you publish it with
NativeAOT for another host. In SDK-style projects `NoWarn` demotes this MSBuild warning to a message
([suppress tool warnings](https://learn.microsoft.com/visualstudio/ide/how-to-suppress-compiler-warnings#suppress-tool-warnings)):

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CESDK9102</NoWarn>
</PropertyGroup>
```
