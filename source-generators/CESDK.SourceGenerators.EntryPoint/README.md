# CESDK.SourceGenerators.EntryPoint

Incremental Roslyn source generator that writes the Cheat Engine entry point into a plugin assembly. It ships in the [
`CESDK` package](../../src/CESDK/README.md) under `analyzers/dotnet/cs`.

## Objective

For the one class marked `[CheatEnginePlugin("Name")]`, the generator emits `CESDK.CESDK.CEPluginInitialize`, the method
Cheat Engine calls to load a plugin. It also emits a file-scoped factory that constructs the plugin class and carries
its display name. Plugin authors never reference the generator.

## Why it exists

Cheat Engine looks up the type `CESDK.CESDK` and the method `CEPluginInitialize` by name, inside the plugin assembly
itself. A referenced library cannot supply them, so the code must be compiled into every plugin. Written by hand, it is
boilerplate, and an exception that escaped it would cross into native code.

## Use

Derive one class from `CheatEnginePlugin` and mark it with the attribute:

```csharp
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;

namespace MyTrainer;

[CheatEnginePlugin("My Trainer")]
public sealed class TrainerPlugin : CheatEnginePlugin
{
    protected override void OnEnable() { }
    protected override void OnDisable() { }
}
```

The build adds `CESDK.EntryPoint.g.cs` to the assembly. Every type name except the file-local `PluginFactory` is
`global::`-qualified, because inside `namespace CESDK` the simple name `CESDK` binds to the generated class. This
excerpt leaves out the header, the pragma lines, the attributes and the XML comments:

```csharp
namespace CESDK
{
    internal static class CESDK
    {
        public static int CEPluginInitialize(global::System.IntPtr args, int size)
        {
            try
            {
                return global::CESDK.Hosting.Bootstrap.PluginHost.InitializeManaged<PluginFactory>(args, size);
            }
            catch (global::System.Exception)
            {
                return 0;
            }
        }
    }

    file sealed class PluginFactory : global::CESDK.Hosting.Plugin.IPluginFactory
    {
        public static global::CESDK.Hosting.Plugin.CheatEnginePlugin Create() => new global::MyTrainer.TrainerPlugin();

        public static global::System.ReadOnlySpan<byte> Utf8Name => "My Trainer"u8;
    }
}
```

## How it works

1. Find the class with `ForAttributeWithMetadataName` on `CESDK.Annotations.Plugin.CheatEnginePluginAttribute`. A class
   without the attribute is not a plugin, and a plugin class in a referenced assembly is never seen.
2. Check the class against the requirements below. The rules live in `PluginShape` of [
   `CESDK.SourceGenerators.Shared`](../CESDK.SourceGenerators.Shared/README.md), which the generator and analyzer [
   `CESDK0001`](../../analyzers/docs/CESDK0001.md) both use.
3. Emit when `CesdkGenerateEntryPoint` is not `false` and exactly one valid plugin class exists. An invalid neighbor
   does not count. Two valid classes, or none, emit nothing, and analyzer [
   `CESDK0002`](../../analyzers/docs/CESDK0002.md) reports the two.
4. Write the file. Name the factory `PluginFactory`, or `GeneratedPluginFactory` when the plugin class is, or sits
   inside, `CESDK.PluginFactory`. The entry point only forwards to `PluginHost.InitializeManaged<TFactory>`, because
   Cheat Engine calls it more than once per load. The init record and idempotency live in [
   `CESDK.Hosting`](../../libs/CESDK.Hosting/README.md).

| Group        | Requirement of the plugin class                                                                                                                                     |
|--------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Kind         | Not `static`, `abstract` or generic, and not nested in a generic type. It derives from `CESDK.Hosting.Plugin.CheatEnginePlugin`, directly or through another class. |
| Reach        | `public`, `internal` or `protected internal` at every nesting level, and not `file`-local.                                                                          |
| Construction | A constructor that `new T()` can call with no arguments. No unset `required` member and no `[Obsolete(error: true)]`.                                               |
| Name         | A non-blank attribute argument. The class is not `CESDK.CESDK` and is not nested in it.                                                                             |

## Requirements

- C# 11 or later, for `file` types, `u8` literals and static abstract interface members. Below C# 11 the compiler
  reports `CS8936` or `CS8706` in the generated file.
- The `CESDK` reference is in the `global` alias. An extern alias alone gives `CS0234` in the generated file.
- To write `CESDK.CESDK` by hand, set `CesdkGenerateEntryPoint` to `false`. Keep plugin code out of the `CESDK`
  namespace (analyzer [`CESDK0004`](../../analyzers/docs/CESDK0004.md)).

## Promise

- The entry point has the exact name Cheat Engine looks up: `CESDK.CESDK.CEPluginInitialize(IntPtr, int)`, returning
  `int`. `NominalOutputTests` checks the symbols and `EntryPointTests` in `tests/CESDK.Tests` checks a packed consumer
  with the switch on and off.
- No exception leaves the entry point. A throwing host call returns 0 (`BootstrapExecutionTests`). The plugin
  constructor runs later, at the first enable, where `PluginHost` catches its exception and fails the enable
  (`EnablePluginTests` in [`CESDK.Hosting.Tests`](../../tests/CESDK.Hosting.Tests/README.md)).
- The factory constructs the plugin with `new`, never through reflection, and returns the display name as the UTF-8
  bytes of the attribute argument (`NominalOutputTests`, `NameEscapingTests`).
- The file holds no `unsafe` code and compiles without errors or warnings for C# 11 through 14 (`NominalOutputTests`,
  `LanguageVersionTests`). It disables `CS0612`, `CS0618` and every ID that `[Experimental]` or
  `[Obsolete(DiagnosticId = ...)]` declares on the plugin class, its enclosing types or its constructor. Such a class
  compiles clean too (`ValidShapeTests`).
- The generator reports no diagnostics (`NominalOutputTests`) and emits nothing for input it cannot serve
  (`NoOutputTests`). An unrelated edit leaves the output cached (`IncrementalityTests`).

## Run the tests

Run `dotnet test --project tests/CESDK.SourceGenerators.EntryPoint.Tests`. The suite compiles the generated file, loads
it and calls the entry point. It needs neither Cheat Engine nor a Lua library. See
the [test project](../../tests/CESDK.SourceGenerators.EntryPoint.Tests/README.md).
