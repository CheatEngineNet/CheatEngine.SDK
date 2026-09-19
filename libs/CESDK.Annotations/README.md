# CESDK.Annotations

Attributes that tell the CESDK generators and analyzers what a plugin class, a Lua function or an SDK API is.

## Objective

Give plugin authors and SDK code one small vocabulary of attributes. The source generators read `[CheatEnginePlugin]`,
`[LuaFunction]` and `[LuaGlobal]` to write code. The analyzers read the same attributes to explain why a plugin or a Lua
binding cannot work. The assembly holds attributes only, with no logic beyond argument checks.

## Why it exists

Generators and analyzers run inside the compiler, so they need markers to find your code. The markers live in a real
assembly instead of generated source. IntelliSense and XML documentation show them, they exist before any generator
runs, and precompiled SDK APIs can carry them.

## How it works

Every namespace starts with `CESDK.Annotations.`.

| Namespace   | Attribute                        | Applies to                                              | Says                                                        | Read by                                                       |
|-------------|----------------------------------|---------------------------------------------------------|-------------------------------------------------------------|---------------------------------------------------------------|
| `Plugin`    | `CheatEnginePlugin(string name)` | class                                                   | This class is the plugin and reports `name` to Cheat Engine | Entry point generator, `CESDK0001`, `CESDK0002`, `CESDK0004`  |
| `Lua`       | `LuaFunction(string name)`       | static method                                           | Export the method as the Lua global `name`                  | Lua bindings generator, `CESDK2001` to `CESDK2003`            |
| `Lua`       | `LuaGlobal(string name)`         | static partial method                                   | Generate a call into the Cheat Engine Lua global `name`     | Lua bindings generator, `CESDK2001`, `CESDK2002`, `CESDK2004` |
| `Lua`       | `LuaClass(string name)`          | class, struct                                           | Names the Cheat Engine Lua class a wrapper type stands for  | Metadata only                                                 |
| `Lua`       | `LuaMethod(string name)`         | method                                                  | Names the object method a wrapper method stands for         | Metadata only                                                 |
| `Lua`       | `LuaProperty(string name)`       | property                                                | Names the object property a wrapper property stands for     | Metadata only                                                 |
| `Lua`       | `LuaStackEffect(int delta)`      | method                                                  | The method changes the Lua stack height by `delta`          | Metadata only                                                 |
| `Threading` | `MainThreadOnly`                 | method, property, constructor, class, struct, interface | Callers must run on the Cheat Engine main thread            | Metadata only                                                 |
| `Threading` | `RunsOnMainThread`               | method                                                  | The body runs on the main thread and restricts no caller    | Metadata only                                                 |
| `Lifetime`  | `RequiresPluginEnabled`          | method, property, constructor, class, struct            | The API works only after Cheat Engine enables the plugin    | Metadata only                                                 |
| `Lifetime`  | `CEOwned`                        | return value, property, parameter                       | Cheat Engine owns the object: do not dispose it             | Metadata only                                                 |

Metadata only means the attribute documents intent in source and in the compiled assembly. The SDK libraries apply
`LuaStackEffect`, `RequiresPluginEnabled`, `MainThreadOnly` and `RunsOnMainThread` to their own APIs.

The Roslyn components target `netstandard2.0` and never reference this assembly. They find each attribute by metadata
name, such as `CESDK.Annotations.Plugin.CheatEnginePluginAttribute`.
`source-generators/CESDK.SourceGenerators.Shared/AnnotationsMetadataNames.cs` spells the names once, and the components
that need them use it.

Each namespace matches its folder, so moving or renaming an attribute changes its metadata name and silences the
generators. Change the type, that file and the tests together.

The compiler stores constructor arguments in metadata and never runs the constructor. `[LuaFunction("")]` compiles, so
the analyzers validate names themselves.

Every attribute is public and stays in metadata, never `[Conditional]`, because analyzers read them from compiled SDK
assemblies. `AllowMultiple` is false on all of them. `AttributeTargets.Method` also admits accessors, local functions
and lambdas, so the Lua bindings generator and analyzers reject those shapes themselves.

`LuaStackEffect`, `MainThreadOnly`, `RunsOnMainThread`, `RequiresPluginEnabled` and `CEOwned` are declared inherited,
but Roslyn does not apply inheritance. A consumer walks overridden members itself. A type-level `MainThreadOnly` or
`RequiresPluginEnabled` does not cover nested types. `CheatEnginePlugin`, `LuaFunction` and `LuaGlobal` are not
inherited: each declaration carries its own attribute.

The project declares no references. The `CESDK` package embeds the assembly and its XML documentation under
`lib/net10.0`.

## Use

```csharp
using System;
using CESDK.Annotations.Lua;
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;
using CESDK.Lua.Runtime;

namespace MyPlugin;

[CheatEnginePlugin("Adder")]
public sealed class AdderPlugin : CheatEnginePlugin
{
    protected override void OnEnable()
    {
        var status = Functions.RegisterLuaFunctions(LuaRuntime.AcquireState());
        if (!status.IsOk) throw new InvalidOperationException($"Lua registration failed: {status}.");
    }

    protected override void OnDisable() { }
}

internal static partial class Functions
{
    [LuaFunction("add")]
    public static long Add(long a, long b) => a + b;
}
```

The build generates `CESDK.CESDK.CEPluginInitialize` and `Functions.RegisterLuaFunctions`. The containing type must be
`partial` (`CESDK2002`), and the plugin namespace must not sit under `CESDK` (`CESDK0004`). Rule pages start at [
`analyzers/docs/README.md`](../../analyzers/docs/README.md).

## Promise

1. Attribute names, namespaces and constructor shapes are the contract of the generators and analyzers.
   `RealAssemblyCompilationTests` and `LuaBindingAnalyzerTests` compile against the real assembly, so a rename or move
   fails the tests.
2. Every public attribute has XML documentation. The build treats compiler warnings, including `CS1591`, as errors.
3. A bad name is reported at compile time: `CESDK0001` for a plugin name and `CESDK2003` for a Lua function name.

## Run the tests

This project has no test project of its own. The consumers compile against the real assembly.

```powershell
dotnet test --project tests/CESDK.SourceGenerators.EntryPoint.Tests
dotnet test --project tests/CESDK.Analyzers.Tests
```
