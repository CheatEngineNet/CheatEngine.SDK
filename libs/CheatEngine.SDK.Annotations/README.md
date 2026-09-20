# CheatEngine.SDK.Annotations

Attributes that tell the CheatEngine.SDK generators and analyzers what a plugin class, a Lua function or an SDK API is.

## Objective

Give plugin authors and SDK code one small vocabulary of attributes. The source generators read `[CheatEnginePlugin]`,
`[LuaFunction]`, `[LuaGlobal]`, `[LuaClass]`, `[LuaMethod]` and `[LuaProperty]` to write code. The analyzers read the
same attributes to explain why a plugin or Lua binding cannot work. The assembly holds attributes only, with no logic
beyond argument checks.

## Why it exists

Generators and analyzers run inside the compiler, so they need markers to find your code. The markers live in a real
assembly instead of generated source. IntelliSense and XML documentation show them, they exist before any generator
runs, and precompiled SDK APIs can carry them.

## How it works

Every namespace starts with `CheatEngine.SDK.Annotations.`.

| Namespace   | Attribute                        | Applies to                                              | Says                                                        | Read by                                                                                   |
|-------------|----------------------------------|---------------------------------------------------------|-------------------------------------------------------------|-------------------------------------------------------------------------------------------|
| `Plugin`    | `CheatEnginePlugin(string name)` | class                                                   | This class is the plugin and reports `name` to Cheat Engine | Entry point generator; `CESDK0001`–`CESDK0005` when the direct bootstrap mode is explicit |
| `Lua`       | `LuaFunction(string name)`       | static method                                           | Export the method as the Lua global `name`                  | Lua bindings generator, `CESDK2001`, `CESDK2003`, `CESDK2005`                             |
| `Lua`       | `LuaGlobal(string name)`         | static partial method                                   | Generate a protected call into Lua global function `name`   | Lua bindings generator, `CESDK2002`, `CESDK2004`                                          |
| `Lua`       | `LuaClass(string name)`          | readonly partial struct                                 | Generate a borrowed `CEObject` handle for CE class `name`   | Lua bindings generator, `CESDK2006`, `CESDK2007`                                          |
| `Lua`       | `LuaMethod(string name)`         | instance partial method on a Lua class handle           | Generate a protected bound-object method call               | Lua bindings generator, `CESDK2006`, `CESDK2007`                                          |
| `Lua`       | `LuaProperty(string name)`       | partial property on a Lua class handle                  | Generate protected object-property accessors                | Lua bindings generator, `CESDK2006`, `CESDK2007`                                          |
| `Lua`       | `LuaStackEffect(int delta)`      | method                                                  | The method changes the Lua stack height by `delta`          | Metadata only                                                                             |
| `Threading` | `MainThreadOnly`                 | method, property, constructor, class, struct, interface | Callers must run on the Cheat Engine main thread            | Metadata only                                                                             |
| `Threading` | `RunsOnMainThread`               | method                                                  | The body runs on the main thread and restricts no caller    | Metadata only                                                                             |
| `Lifetime`  | `RequiresPluginEnabled`          | method, property, constructor, class, struct            | The API works only after Cheat Engine enables the plugin    | Lifecycle analyzer, `CESDK1001`                                                           |
| `Lifetime`  | `CEOwned`                        | return value, property, parameter                       | Cheat Engine owns the object: do not dispose it             | Ownership analyzer, `CESDK1003`                                                           |

Metadata only means the attribute documents intent in source and in the compiled assembly. The SDK libraries apply
`LuaStackEffect`, `RequiresPluginEnabled`, `MainThreadOnly` and `RunsOnMainThread` to their own APIs.

The Roslyn components target `netstandard2.0` and never reference this assembly. They resolve each annotation through
its actual compilation symbol, using its metadata name such as
`CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute` only as the lookup key. A look-alike source type is
not an SDK annotation.
`source-generators/CheatEngine.SDK.SourceGenerators.Shared/AnnotationsMetadataNames.cs` spells the names once, and the
components that need them use it.

Each namespace matches its folder, so moving or renaming an attribute changes its metadata name and silences the
generators. Change the type, that file and the tests together.

The compiler stores constructor arguments in metadata and never runs the constructor. `[LuaFunction("")]` compiles, so
the analyzers validate names themselves.

Every attribute is public and stays in metadata, never `[Conditional]`, because analyzers read them from compiled SDK
assemblies. `Inherited` and `AllowMultiple` are explicit on every attribute; the latter is false throughout.
`AttributeTargets.Method` also admits accessors, local functions and lambdas, so the Lua bindings generator and
analyzers reject those shapes themselves. `[LuaGlobal]` deliberately targets methods only: a future Lua global-variable
feature needs a distinct contract.

`LuaStackEffect`, `MainThreadOnly`, `RunsOnMainThread`, `RequiresPluginEnabled` and `CEOwned` are declared inherited,
but Roslyn does not apply inheritance. A consumer walks overridden members itself. A type-level `MainThreadOnly` or
`RequiresPluginEnabled` does not cover nested types. `CheatEnginePlugin`, `LuaFunction`, `LuaGlobal`, `LuaClass`,
`LuaMethod` and `LuaProperty` are not inherited: each declaration carries its own attribute.

`[LuaClass]` is a borrowed handle only. The generated readonly struct implements `ICEObject<T>` and
`ILuaMarshaller<T>` around `CEObject`; `Owned<T>` is the one representation of plugin ownership and deterministic
`destroy()`.

The project declares no references. The `CheatEngine.SDK` package embeds the assembly and its XML documentation under
`lib/net10.0`.

## Use

```csharp
using System;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Runtime;

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

The build generates `CESDK.CESDK.CEPluginInitialize`, the bootstrap type that Cheat Engine requires in a plugin
assembly, and `Functions.RegisterLuaFunctions`. The containing type must be `partial` (`CESDK2002`), and the plugin
namespace must not be `CESDK` or sit under it (`CESDK0004`), because inside that namespace the simple name `CESDK`
binds to the generated class, and a qualified name that starts with `CESDK.` no longer resolves (CS0426). The SDK's own
`CheatEngine.SDK` namespaces are not affected. Rule pages start at [
`analyzers/docs/README.md`](../../analyzers/docs/README.md).

## Promise

1. Attribute names, namespaces and constructor shapes are the contract of the generators and analyzers.
   `RealAssemblyCompilationTests` and `LuaBindingAnalyzerTests` compile against the real assembly, so a rename or move
   fails the tests.
2. Every public attribute has XML documentation. The build treats compiler warnings, including `CS1591`, as errors.
3. A bad name or non-generable Lua declaration is reported at compile time. `CESDK2003` covers function shape,
   `CESDK2004` global shape, and `CESDK2006`/`CESDK2007` class-handle forms and generated-identity collisions.

## Run the tests

This project has no test project of its own. The consumers compile against the real assembly.

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EntryPoint.Tests
dotnet test --project tests/CheatEngine.SDK.Analyzers.Tests
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests
```
