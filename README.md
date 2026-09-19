<div align="center">

# CheatEngine.SDK

**Write Cheat Engine plugins as ordinary C# classes.**

[![Build](https://img.shields.io/github/actions/workflow/status/CheatEngineNet/CheatEngine.SDK/merge.yml?branch=main&style=flat-square&logo=githubactions&logoColor=white&labelColor=24292f)](https://github.com/CheatEngineNet/CheatEngine.SDK/actions/workflows/merge.yml)
[![NuGet](https://img.shields.io/nuget/vpre/CheatEngine.SDK?style=flat-square&logo=nuget&logoColor=white&labelColor=24292f&color=004880)](https://www.nuget.org/packages/CheatEngine.SDK)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white&labelColor=24292f)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square&labelColor=24292f)](#requirements)
[![MIT license](https://img.shields.io/badge/license-MIT-6e7781?style=flat-square&labelColor=24292f)](LICENSE)

[Quick start](#quick-start) · [Why CheatEngine.SDK exists](#why-cheatenginesdk-exists) · [Requirements](#requirements) · [Projects](#projects) · [Contributing](#contributing)

</div>

## What CheatEngine.SDK is

CheatEngine.SDK is a .NET 10 SDK for writing Cheat Engine plugins in C#. You add one NuGet package, derive one class,
and build. The package generates the entry point Cheat Engine loads, exposes your static methods to Lua, and reports
plugin mistakes in the editor before you open Cheat Engine.

## Why CheatEngine.SDK exists

Cheat Engine's own C# template asks every plugin to compile over a thousand lines of interop code into itself, marshal
native structures by hand, and write the exported entry point exactly right. A wrong shape gives no error message: Cheat
Engine simply refuses to load the plugin.

CheatEngine.SDK moves that work into a package. The entry point and the Lua bindings are generated at compile time,
without reflection, and analyzers explain what is wrong while you type. Your plugin stays a plain class.

## Who it is for

C# developers who build plugins, tools, and automation for Cheat Engine 7.7 on Windows x64 and want typed,
compiler-checked code instead of hand-written interop. Lua remains the fastest way to script Cheat Engine without a
build step. CheatEngine.SDK is for plugins that deserve a real project.

## Quick start

1. Create a class library, target x64, and add the package.

   ```powershell
   dotnet new classlib -n MyPlugin
   cd MyPlugin
   dotnet add package CheatEngine.SDK --prerelease
   ```

   Add `<PlatformTarget>x64</PlatformTarget>` to the `PropertyGroup` of `MyPlugin.csproj` and delete `Class1.cs`.

2. Add a plugin class with one Lua function.

   ```csharp
   using CheatEngine.SDK.Annotations.Lua;
   using CheatEngine.SDK.Annotations.Plugin;
   using CheatEngine.SDK.Hosting.Plugin;
   using CheatEngine.SDK.Lua.Runtime;

   namespace MyPlugin;

   [CheatEnginePlugin("My Plugin")]
   public sealed class HelloPlugin : CheatEnginePlugin
   {
       protected override void OnEnable() => Commands.RegisterLuaFunctions(LuaRuntime.AcquireState());

       protected override void OnDisable() => Commands.UnregisterLuaFunctions(LuaRuntime.AcquireState());
   }

   internal static partial class Commands
   {
       [LuaFunction("greet")]
       public static string Greet(string name) => $"Hello, {name}!";
   }
   ```

3. Build, then keep the whole output folder together. The CheatEngine.SDK libraries and
   `cheatengine-sdk-lua-bridge.dll` sit next to `MyPlugin.dll`.

   ```powershell
   dotnet build -c Release
   ```

4. Start Cheat Engine, add `MyPlugin.dll` in the plugin settings, and enable it. In the Lua engine window, run
   `print(greet("world"))`.

> [!IMPORTANT]
> Cheat Engine 7.7 asks for .NET 9. Before starting it, edit the Cheat Engine folder's `ce.runtimeconfig.json` in an
> elevated editor to request .NET 10 explicitly:
>
> - Set `runtimeOptions.tfm` to `net10.0`.
> - Set the `version` of every framework request to `10.0.0`: `Microsoft.NETCore.App`,
>   `Microsoft.WindowsDesktop.App`, and `Microsoft.AspNetCore.App` (whether the file uses `framework` or `frameworks`).
> - Set `runtimeOptions.rollForward` to `LatestMinor`. If a framework entry has its own `rollForward`, set it to
>   `LatestMinor` too.
>
> With that configuration, Cheat Engine stays on .NET 10 even when .NET 9 or 11 is installed. A shell launch can repeat
> the same policy, but does not select .NET 10 by itself:
>
> ```powershell
> $env:DOTNET_ROLL_FORWARD = "LatestMinor"
> .\cheatengine-x86_64.exe
> ```
>
> Keep the existing framework names. This changes Cheat Engine's runtime request only; it does not change installed
> runtimes or machine-wide environment settings.

The [live plugin guide](tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine) walks through the same
steps with a larger sample and the log output to expect.

## How it works

| You write                                                                      | CheatEngine.SDK provides                                                                                                 |
|--------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------|
| `[CheatEnginePlugin("Name")]` on a class that derives from `CheatEnginePlugin` | The `CEPluginInitialize` entry point Cheat Engine looks up, generated into your assembly                                 |
| `[LuaFunction("name")]` on a static method                                     | A Lua global with its native thunk, registered and unregistered for you                                                  |
| `[LuaGlobal]` on a partial method                                              | A typed call into a Cheat Engine Lua function such as `readInteger`                                                      |
| A plugin that Cheat Engine would refuse                                        | An editor diagnostic from `CESDK0001` to `CESDK2004`, each with a [page that explains the fix](analyzers/docs/README.md) |
| An exception inside your plugin                                                | A logged failure instead of a crash in Cheat Engine                                                                      |

## Requirements

| Requirement   | Version                                                                                         |
|---------------|-------------------------------------------------------------------------------------------------|
| .NET SDK      | 10.0.401 or later                                                                               |
| .NET runtimes | .NET 10 `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App`, and `Microsoft.AspNetCore.App` |
| Cheat Engine  | 7.7                                                                                             |
| Platform      | Windows, x64                                                                                    |

The analyzers and generators are built against Roslyn 5.9. An older SDK reports `CS9057` and skips them, so the entry
point is never generated.

Plugin authors and ordinary source builds do not need xmake or a C compiler. The package and repository carry the
prebuilt Windows x64 Lua protection bridge. CI rebuilds that bridge with xmake before it builds, tests, and packs
CheatEngine.SDK; only contributors changing `native/cheatengine-sdk-lua-bridge` need the native toolchain locally.

## Projects

CheatEngine.SDK ships as one NuGet package built from small layered libraries.

| Project                                                                                        | Role                                                                        |
|------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------|
| [`libs/CheatEngine.SDK.Annotations`](libs/CheatEngine.SDK.Annotations/README.md)               | The attributes that generators and analyzers read                           |
| [`libs/CheatEngine.SDK.Abi`](libs/CheatEngine.SDK.Abi/README.md)                               | The binary layout of Cheat Engine's plugin interface                        |
| [`libs/CheatEngine.SDK.Lua.Interop`](libs/CheatEngine.SDK.Lua.Interop/README.md)               | The raw Lua 5.3 C API, bound to the Lua library Cheat Engine already loaded |
| [`libs/CheatEngine.SDK.Lua`](libs/CheatEngine.SDK.Lua/README.md)                               | Managed Lua state, protected calls, marshallers, and callbacks              |
| [`libs/CheatEngine.SDK.Engine`](libs/CheatEngine.SDK.Engine/README.md)                         | The Cheat Engine object model: handles, ownership, addresses, and enums     |
| [`libs/CheatEngine.SDK.Hosting`](libs/CheatEngine.SDK.Hosting/README.md)                       | The plugin lifecycle, main thread access, and logging                       |
| [`source-generators`](source-generators/CheatEngine.SDK.SourceGenerators.EntryPoint/README.md) | The entry point and Lua binding generators                                  |
| [`analyzers`](analyzers/CheatEngine.SDK.Analyzers/README.md)                                   | The `CESDKnnnn` diagnostics and their code fixes                            |
| [`src/CheatEngine.SDK`](src/CheatEngine.SDK/README.md)                                         | The single NuGet package that embeds all of the above                       |

## Contributing

```powershell
dotnet build CheatEngine.SDK.slnx
dotnet test --solution CheatEngine.SDK.slnx
dotnet pack src/CheatEngine.SDK -c Release
```

Warnings are errors, so the build is also the lint step. Tests tagged `Category=NativeLua` run against the Lua DLL of
Cheat Engine 7.7 kept in [`native/cheat-engine`](native/cheat-engine/README.md), so nothing has to be installed;
[`tests/CheatEngine.SDK.Tests.Shared`](tests/CheatEngine.SDK.Tests.Shared/README.md) explains how the DLL is found.
Every project has a README
that states its design and guarantees.

To rebuild the bridge after changing its C source, install xmake and a Windows x64 C toolchain, then follow
[`native/cheatengine-sdk-lua-bridge/README.md`](native/cheatengine-sdk-lua-bridge/README.md). Normal managed changes do
not require this.

## License

[MIT](LICENSE). CheatEngine.SDK is an independent project and is not affiliated with Cheat Engine, which is licensed
separately. The repository keeps one Cheat Engine file, the Lua DLL of
[`native/cheat-engine`](native/cheat-engine/README.md), as a test fixture. It stays under Cheat Engine's terms, this
license does not cover it, and the NuGet package does not contain it.
