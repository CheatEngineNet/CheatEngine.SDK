<div align="center">

# CheatEngine.SDK

**Build Cheat Engine plugins as ordinary C# classes.**

[![Build](https://img.shields.io/github/actions/workflow/status/CheatEngineNet/CheatEngine.SDK/main-ci.yml?branch=main&style=flat-square&logo=githubactions&logoColor=white&labelColor=24292f)](https://github.com/CheatEngineNet/CheatEngine.SDK/actions/workflows/main-ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/CheatEngine.SDK?style=flat-square&logo=nuget&logoColor=white&labelColor=24292f&color=004880)](https://www.nuget.org/packages/CheatEngine.SDK)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white&labelColor=24292f)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square&labelColor=24292f)](#requirements)
[![MIT license](https://img.shields.io/badge/license-MIT-6e7781?style=flat-square&labelColor=24292f)](LICENSE)

[Quick start](#quick-start) · [How it works](#how-it-works) · [Compatibility](#compatibility-and-deployment) · [Projects](#projects) · [Contributing](#contributing)

</div>

## What CheatEngine.SDK is

CheatEngine.SDK is a .NET 10 SDK for writing Cheat Engine plugins in C#. You add one NuGet package, derive one class,
and build. The SDK generates the entry point Cheat Engine loads, exposes static C# methods to Lua, and reports plugin
mistakes in the editor before Cheat Engine starts.

## Why CheatEngine.SDK exists

Cheat Engine's own C# template asks every plugin to compile over a thousand lines of interop code into itself, marshal
native structures by hand, and write the exported entry point exactly right. A wrong shape gives no error message: Cheat
Engine simply refuses to load the plugin.

CheatEngine.SDK moves that work into a package. The entry point and Lua bindings are generated at compile time, without
reflection, while analyzers explain invalid plugin shapes in the editor. Your plugin remains a plain class.

## Who it is for

C# developers building plugins, tools, and automation for Cheat Engine 7.7 on Windows x64 who want typed,
compiler-checked code instead of hand-written interop. Lua remains excellent for quick scripts; this SDK is for plugins
that benefit from a testable .NET project.

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

3. Build, then deploy the complete output directory as one unit. `MyPlugin.dll`, its
   `.deps.json` and `.runtimeconfig.json`, the CheatEngine.SDK assemblies, and
   `cheatengine-sdk-lua-bridge.dll` must remain together.

   ```powershell
   dotnet build -c Release
   ```

4. In a controlled Cheat Engine 7.7 x64 test host, add `MyPlugin.dll` in the plugin settings and enable it. In the Lua
   Engine, run `print(greet("world"))`.

> [!IMPORTANT]
> Establish the Cheat Engine runtime policy in the controlled environment that loads the plugin. Do not treat a local
> `ce.runtimeconfig.json` captured during development as a universal installer configuration.

The [live plugin guide](tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine) walks through the same
steps with a larger sample and the log output to expect.

## How it works

| You write                                                                      | CheatEngine.SDK provides                                                                                                 |
|--------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------|
| `[CheatEnginePlugin("Name")]` on a class that derives from `CheatEnginePlugin` | The `CEPluginInitialize` entry point Cheat Engine looks up, generated into your assembly                                 |
| `[LuaFunction("name")]` on a static method                                     | A Lua global with its native thunk, registered and unregistered for you                                                  |
| `[LuaGlobal]` on a static partial method                                       | A typed, protected call into a Cheat Engine Lua function such as `readInteger`                                          |
| An invalid plugin declaration                                                  | An actionable `CESDK` diagnostic, with [rule documentation](analyzers/docs/README.md)                                  |
| An exception inside your plugin                                                | A logged failure instead of a crash in Cheat Engine                                                                      |

## Requirements

| Requirement   | Version                                                                                         |
|---------------|-------------------------------------------------------------------------------------------------|
| .NET SDK      | 10.0.401 or later                                                                               |
| .NET runtimes | .NET 10 `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App`, and `Microsoft.AspNetCore.App` |
| Cheat Engine  | 7.7 (`7.7.0.10621` is the recorded compatibility baseline)                                      |
| Platform      | Windows, x64                                                                                    |

The analyzers and generators are built against Roslyn 5.9. An older SDK reports `CS9057` and skips them, so the entry
point is never generated.

Plugin authors and managed-only contributors do not need xmake or a C compiler. The package carries a prebuilt Windows
x64 Lua protection bridge; only changes under `native/cheatengine-sdk-lua-bridge` require the native toolchain.

## Compatibility and deployment

Cheat Engine is the compatibility authority. Live checks are opt-in and do not run in normal CI. The generated
bootstrap keeps the host signature `CEPluginInitialize(IntPtr, int)`, but its second argument is deliberately forwarded
as an opaque host value until an exact CE 7.7 live probe establishes its meaning.

Deploy the complete output folder as one unit. The package's generators, native bridge, and dynamic-loading defaults
apply to a direct `CheatEngine.SDK` package reference, keeping those plugin-specific behaviors at the actual host
boundary rather than flowing through indirect dependencies.

The SDK is trim- and AOT-friendly where its analyzers and publish probe validate the managed graph. This is not a claim
that a Cheat Engine-loaded plugin is a Native AOT DLL: the supported form is a framework-dependent managed plugin folder
with its native bridge. The intentionally small [Lua protection bridge](native/cheatengine-sdk-lua-bridge/README.md)
contains the C11 boundary needed to contain Lua `longjmp` failures, uses Cheat Engine's already-loaded Lua runtime, and
does not load a second one. Package layout and deployment details are documented by the
[umbrella package](src/CheatEngine.SDK/README.md).

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
