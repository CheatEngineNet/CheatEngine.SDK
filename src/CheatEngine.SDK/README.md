# CheatEngine.SDK

Write a Cheat Engine plugin as an ordinary C# class.

## Objective

CheatEngine.SDK lets a C# developer write a Cheat Engine plugin as an ordinary .NET class, from one NuGet package. You
derive one class from `CheatEnginePlugin`, mark it `[CheatEnginePlugin("Name")]`, and override `OnEnable` and
`OnDisable`. Static methods marked `[LuaFunction("name")]` become Lua globals once `OnEnable` calls the generated
`RegisterLuaFunctions`.

## Why it exists

Cheat Engine looks for a type `CESDK.CESDK` with a method `CEPluginInitialize` in the plugin assembly. Cheat Engine
refuses to load a plugin whose entry point has the wrong shape. Writing that entry point, the native interop behind it,
and the Lua glue by hand repeats the same work in every plugin. CheatEngine.SDK generates the entry point and the Lua
bindings at compile time, and analyzers explain the common mistakes in the editor. Every part shares one version
because the parts are built and packed together.

## How to use it

1. Create a class library and add the package.
   ```powershell
   dotnet new classlib -n MyPlugin
   cd MyPlugin
   dotnet add package CheatEngine.SDK --version 1.0.0
   ```
2. Add the following to the `PropertyGroup` of `MyPlugin.csproj`, then delete `Class1.cs`:
   ```xml
   <PlatformTarget>x64</PlatformTarget>
   <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
   ```
   The second setting is an explicit opt-in for the `[LuaFunction]` registration thunk in the next step; the package
   deliberately leaves unsafe compilation disabled for projects that do not export a Lua function.
3. Add a plugin class with one Lua function. Keep your code in a namespace that does not start with `CESDK`: Cheat
   Engine requires the type `CESDK.CESDK`, which the SDK generates, and inside the `CESDK` namespace the simple name
   `CESDK` binds to that class. `CESDK0004` warns about such a namespace.
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
4. Run `dotnet build -c Release` and keep the complete `bin/Release/net10.0` folder together. The plugin DLL, SDK
   assemblies, `.deps.json`, `.runtimeconfig.json`, and `cheatengine-sdk-lua-bridge.dll` form one deployment unit.
5. In an exact, controlled Cheat Engine 7.7 x64 test host, add `MyPlugin.dll` in the plugin settings, enable it, then
   run `print(greet("world"))` in the Lua Engine. Follow the live-plugin guide for the host-side observation procedure.

> [!IMPORTANT]
> A `ce.runtimeconfig.json` captured in this workspace is a local .NET 10 modification, not an installer artifact whose
> framework request can be prescribed for every CE 7.7 installation. The inspected host follows the
> `nethost`/`hostfxr` route, and Microsoft documents that route for framework-dependent components. Do not overwrite an
> installed Cheat Engine runtime configuration from this package guide. Establish and record the host runtime policy in
> the controlled environment that performs the opt-in live verification. The
> [support profile](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/docs/qualification/support-profile.md#runtime-policy)
> records the observed policy and its hash.

The [live plugin guide](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine)
has the controlled observation procedure; it does not establish a runtime-configuration recipe for arbitrary CE
installations.

## What is inside

`lib/net10.0` holds the six libraries (`Abi`, `Annotations`, `Engine`, `Hosting`, `Lua`, `Lua.Interop`) with their XML
docs, plus an empty `CheatEngine.SDK.dll`. `analyzers/dotnet/cs` holds `Analyzers`, `Analyzers.CodeFixes`,
`SourceGenerators.EntryPoint`, `SourceGenerators.LuaBindings` and their `SourceGenerators.Shared` dependency.
`build/` holds the direct-consumer props and targets,
with its prebuilt Lua protection bridge at `build/native/cheatengine-sdk-lua-bridge.dll`. There is deliberately no
`buildTransitive/` asset and no `runtimes/win-x64/native` asset: an indirect dependency must not silently generate a
plugin entry point, change compiler settings, or copy native files into another project's deployment. The `EngineApi`
generator is repository-internal and never ships. The package has no NuGet dependencies and consumers need no C
compiler or xmake.

For a direct `PackageReference`, `CheatEngine.SDK.props` sets `EnableDynamicLoading=true` and
`CheatEngineSdkGenerateEntryPoint=true`, each only while your project leaves the property empty. It does not set
`AllowUnsafeBlocks`: add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` only when your project declares a
`[LuaFunction]`; globals and generated object handles compile with the normal `false` setting. Without the direct
build asset, the entry-point generator is intentionally silent. Set `CheatEngineSdkGenerateEntryPoint` to `false` to
write the exact manual `CESDK.CESDK.CEPluginInitialize(System.IntPtr, int)` contract; `CESDK0003` validates that form.
`CESDK9101` from the packaged `CheatEngine.SDK.targets` accepts an unset `PlatformTarget`, `AnyCPU`, or `x64`, and
fails every other explicit target (`x86`, `ARM`, `ARM64`, `Itanium`, and unknown values). An AnyCPU class library is
valid because Cheat Engine loads it inside its x64 process; the package's native Lua protection bridge itself remains
Windows x64-only. These build assets never flow through an intermediate NuGet package.

## Promise

- The package has no NuGet dependencies, so one direct `PackageReference` is enough (`NuspecDependencyTests`).
- `analyzers/dotnet/cs` holds only the shipping analyzers, generators and their shared loader dependency, never the
  `EngineApi` generator (`PackageContentsTests`). Its build props, targets and native bridge are direct-reference-only:
  a real relay package
  cannot apply them to the relay's consumer (`DirectReferenceIsolationTests`).
- A plugin project with one `[CheatEnginePlugin]` class gets the generated `CESDK.CESDK.CEPluginInitialize` from the
  package reference alone. `CheatEngineSdkGenerateEntryPoint=false` switches it off (`EntryPointTests`).
- The native protection bridge is copied into both build and publish output from the package using normal MSBuild
  content items; after a clean rebuild, the plugin, SDK assemblies, manifests and bridge share one deployment folder
  (`EntryPointTests`, `DeploymentLayoutTests`).
- `AllowUnsafeBlocks` stays under the consumer's control; it is necessary only for `[LuaFunction]` exports
  (`BuildPropertyDefaultsTests` and `LuaObjectOutputTests`).
- A direct plugin may leave `PlatformTarget` unset or set it to `AnyCPU` or `x64`; every other explicit architecture
  fails the build with `CESDK9101` (target `CheatEngineSdkRequireX64Platform` in
  `build/CheatEngine.SDK.targets`; `PlatformTargetTests`).
- Every diagnostic has a help link to its own rule page, listed in
  the [rule index](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/README.md)
  (`DiagnosticCatalogTests`).
- An exception from `OnEnable` is logged and reported to Cheat Engine as a failed call. An `OnDisable` exception is
  logged, cleanup still completes, and Cheat Engine receives success to record the disabled state. Neither propagates
  into Cheat Engine (`EnablePluginTests` and `DisablePluginTests`, which run against Cheat Engine's own Lua DLL).

## AOT status

The shipping libraries set `IsAotCompatible=true` and verify that their runtime references carry equivalent AOT
metadata. `tests/CheatEngine.SDK.AotProbe` is a standalone Windows x64 executable that publishes the complete shipping
graph with Native AOT. A successful probe establishes only the analysed graph and that publish invocation; it is not a
Cheat Engine plugin and says nothing about whether CE can host or unload a Native AOT artifact. In particular, Native
AOT class-library exports require explicit `UnmanagedCallersOnly` exports and Native AOT DLLs do not support
`FreeLibrary`
unloading. [Microsoft's Native AOT library guidance](https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries)
and [single-file deployment guidance](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview) describe
different deployment models from this framework-dependent plugin folder.

## Requirements

| Requirement   | Version                                                                                                                       |
|---------------|-------------------------------------------------------------------------------------------------------------------------------|
| .NET SDK      | 10.0.401 or later                                                                                                             |
| .NET runtimes | .NET 10 `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and `Microsoft.AspNetCore.App` (see `dotnet --list-runtimes`) |
| Cheat Engine  | 7.7, Windows, x64                                                                                                             |

The qualifiable host profile is `ce-7.7.0.10621-x64-managed-hostfxr`, described in the
[support profile](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/docs/qualification/support-profile.md).
Host qualification is recorded per scenario in the
[qualification matrix](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/docs/qualification/README.md#matrix-summary);
a scenario without a committed receipt is not executed.

The analyzers and generators are built against Roslyn 5.9.0. An older SDK reports `CS9057` and skips them, so no entry
point is generated.

CheatEngine.SDK is an independent project, not affiliated with Cheat Engine, which is licensed separately. This package
contains no Cheat Engine file. See the
[MIT license](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/LICENSE) and the
[source repository](https://github.com/CheatEngineNet/CheatEngine.SDK).
