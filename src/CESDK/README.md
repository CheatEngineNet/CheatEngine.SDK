# CESDK

Write a Cheat Engine plugin as an ordinary C# class.

## Objective

CESDK lets a C# developer write a Cheat Engine plugin as an ordinary .NET class, from one NuGet package. You derive one
class from `CheatEnginePlugin`, mark it `[CheatEnginePlugin("Name")]`, and override `OnEnable` and `OnDisable`. Static
methods marked `[LuaFunction("name")]` become Lua globals once `OnEnable` calls the generated `RegisterLuaFunctions`.

## Why it exists

Cheat Engine looks for a type `CESDK.CESDK` with a method `CEPluginInitialize` in the plugin assembly. Cheat Engine
refuses to load a plugin whose entry point has the wrong shape. Writing that entry point, the native interop behind it,
and the Lua glue by hand repeats the same work in every plugin. CESDK generates the entry point and the Lua bindings at
compile time, and analyzers explain the common mistakes in the editor. Every part shares one version because the parts
are built and packed together.

## How to use it

1. Create a class library and add the package.
   ```powershell
   dotnet new classlib -n MyPlugin
   cd MyPlugin
   dotnet add package CESDK --prerelease
   ```
2. Add `<PlatformTarget>x64</PlatformTarget>` to the `PropertyGroup` of `MyPlugin.csproj`, and delete `Class1.cs`.
3. Add a plugin class with one Lua function. Keep your code out of the `CESDK` namespace: the generated entry point is
   `CESDK.CESDK`, and `CESDK0004` warns about the namespace.
   ```csharp
   using CESDK.Annotations.Lua;
   using CESDK.Annotations.Plugin;
   using CESDK.Hosting.Plugin;
   using CESDK.Lua.Runtime;

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
4. Run `dotnet build -c Release` and keep the whole `bin/Release/net10.0` folder together. The CESDK assemblies and
   `cesdk-lua-bridge.dll` sit next to `MyPlugin.dll`.
5. Configure Cheat Engine for .NET 10 as described below, then start it, add `MyPlugin.dll` in the plugin settings, and enable
   it. In the Lua Engine window, run `print(greet("world"))`.

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

The [live plugin guide](https://github.com/ShadowNineX/CESDK/blob/main/tests/CESDK.LivePlugin/README.md#run-it-in-cheat-engine)
has the full Cheat Engine procedure.

## What is inside

`lib/net10.0` holds the six libraries (`Abi`, `Annotations`, `Engine`, `Hosting`, `Lua`, `Lua.Interop`) with their XML
docs, plus an empty `CESDK.dll`. `analyzers/dotnet/cs` holds `Analyzers`, `Analyzers.CodeFixes`,
`SourceGenerators.EntryPoint` and `SourceGenerators.LuaBindings`. `build/` and `buildTransitive/` hold `CESDK.props`.
`runtimes/win-x64/native` holds the prebuilt Lua protection bridge. The `EngineApi` generator is repository-internal
and never ships. The package has no NuGet dependencies and consumers need no C compiler or xmake.

`CESDK.props` sets `AllowUnsafeBlocks=true`, `EnableDynamicLoading=true` and `CesdkGenerateEntryPoint=true`, each only
while your project leaves the property empty. Set `CesdkGenerateEntryPoint` to `false` to write `CESDK.CESDK` by hand.
`CESDK9101` fails the build when a plugin sets `PlatformTarget=x86`.

## Promise

- The package has no NuGet dependencies, so one `PackageReference` is enough (`NuspecDependencyTests`).
- `analyzers/dotnet/cs` holds only the shipping analyzers and generators, never the `EngineApi` generator
  (`PackageContentsTests`).
- A plugin project with one `[CheatEnginePlugin]` class gets the generated `CESDK.CESDK.CEPluginInitialize` from the
  package reference alone. `CesdkGenerateEntryPoint=false` switches it off (`EntryPointTests`).
- The native protection bridge is copied into both build and publish output from the package (`EntryPointTests`).
- Your own MSBuild values win over the package defaults (`BuildPropertyDefaultsTests` covers `AllowUnsafeBlocks`).
- A plugin that sets `PlatformTarget=x86` fails the build with `CESDK9101` (target `CesdkRequireX64Platform` in
  `build/CESDK.props`).
- Every diagnostic has a help link to its own rule page, listed in
  the [rule index](https://github.com/ShadowNineX/CESDK/blob/main/analyzers/docs/README.md) (`DiagnosticCatalogTests`).
- An exception from `OnEnable` is logged and reported to Cheat Engine as a failed call. An `OnDisable` exception is
  logged, cleanup still completes, and Cheat Engine receives success to record the disabled state. Neither propagates
  into Cheat Engine (`EnablePluginTests` and `DisablePluginTests`, which run against Cheat Engine's own Lua DLL).

## Requirements

| Requirement   | Version                                                                                                                       |
|---------------|-------------------------------------------------------------------------------------------------------------------------------|
| .NET SDK      | 10.0.401 or later                                                                                                             |
| .NET runtimes | .NET 10 `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and `Microsoft.AspNetCore.App` (see `dotnet --list-runtimes`) |
| Cheat Engine  | 7.7, Windows, x64                                                                                                             |

The analyzers and generators are built against Roslyn 5.9.0. An older SDK reports `CS9057` and skips them, so no entry
point is generated.

CESDK is an independent project, not affiliated with Cheat Engine, which is licensed separately. This package contains
no Cheat Engine file. See the [MIT license](https://github.com/ShadowNineX/CESDK/blob/main/LICENSE) and
the [source repository](https://github.com/ShadowNineX/CESDK).
