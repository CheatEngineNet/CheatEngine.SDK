# CESDK0001: Plugin class cannot be constructed by the generated entry point

|                    |                                                                 |
|--------------------|-----------------------------------------------------------------|
| Category           | `CheatEngine.SDK.Plugin`                                        |
| Default severity   | Error                                                           |
| Enabled by default | Yes                                                             |
| Code fix           | Yes: three actions for the mechanical problems (see Code fixes) |
| Reported           | While typing and in build                                       |

## Cause

A class carries `[CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin]`, but the entry point that the SDK generates into the
plugin assembly cannot create it.

## Why

Cheat Engine calls one fixed method of the plugin assembly, `CESDK.CESDK.CEPluginInitialize`. The SDK generates that
method together with a small factory that does, in effect, `new global::YourNamespace.YourPlugin()` and hands the object
to the hosting layer. The factory is a top-level type in a generated file of the same assembly and has no inheritance
relation to your class. Everything the rule asks for follows from that one expression having to compile and to yield a
`CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin`.

When the class does not qualify, the generator emits nothing. Without this rule the only symptom would be a plugin that
Cheat Engine refuses to load. The rule names the cause where it is, at the class.

## What is checked

One diagnostic is reported per problem, on the class name of the part that carries the attribute (on the attribute
itself for the display name):

| Problem                                | Requirement                                                                                                                                                                                                                                                                                                                                                                    |
|----------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Static`                               | The class is not `static`. A static class has no base class and no instance constructor, so only this problem and a bad display name are reported for it.                                                                                                                                                                                                                      |
| `Abstract`                             | The class is not `abstract`.                                                                                                                                                                                                                                                                                                                                                   |
| `Generic`                              | The class has no type parameters.                                                                                                                                                                                                                                                                                                                                              |
| `NestedInGeneric`                      | No type the class is nested in has type parameters.                                                                                                                                                                                                                                                                                                                            |
| `NotDerivedFromPluginBase`             | `CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin` is somewhere in the base-class chain. A `record` can never satisfy this, because a record cannot inherit from a class.                                                                                                                                                                                                      |
| `Inaccessible`                         | The class and every type it is nested in are `public`, `internal` or `protected internal`. `private`, `protected` and `private protected` hide it from the generated factory.                                                                                                                                                                                                  |
| `FileLocal`                            | Neither the class nor a type it is nested in is a `file` type.                                                                                                                                                                                                                                                                                                                 |
| `ReservedEntryPointName`               | The class is not, and is not nested in, the top-level type `CESDK.CESDK`. Cheat Engine dictates that name for the generated entry point type, and an assembly holds one type of a name. `MyPlugin.CESDK` and `CESDK.Samples.CESDK` are other types and fine. The namespace `CESDK` itself is what [CESDK0004](CESDK0004.md) is about.                                          |
| `MissingParameterlessConstructor`      | If constructors are declared, one is an actual parameterless constructor. Constructors containing optional or `params` parameters do not qualify. A primary constructor with an empty parameter list counts.                                                                 |
| `InaccessibleParameterlessConstructor` | At least one actual parameterless constructor exists, but every such constructor is `private`, `protected` or `private protected`. A constructor without an accessibility keyword is `private`. When several parameterless constructors exist, only an accessible one matters.                                      |
| `RequiredMembers`                      | Neither the class nor a base class declares `required` fields or properties, unless the parameterless constructor carries `[SetsRequiredMembers]`. The generated `new YourPlugin()` has no object initializer, so required members would fail with CS9035. Reported next to `MissingParameterlessConstructor` when both apply: adding a plain constructor would not be enough. |
| `ObsoleteError`                        | Neither the class, a type it is nested in, nor its parameterless constructor is marked `[Obsolete(..., error: true)]`. Naming such a symbol would be CS0619, which no `#pragma` can silence. `[Obsolete]` as a warning is fine: the generated file disables CS0612 and CS0618.                                                                                                 |
| `InvalidName`                          | The display name passed to the attribute is not `null`, empty or white space. It is the name Cheat Engine shows in its plugin list.                                                                                                                                                                                                                                            |

A class without any declared constructor is fine: the implicit constructor is public. On an abstract class the implicit
constructor is protected. Only `Abstract` is reported, because the constructor becomes public as soon as `abstract` is
gone.

One limit applies to `RequiredMembers` and `ObsoleteError`. When several parameterless constructors exist, both are
read from one of them only. That constructor is the first accessible one, or the first one when none is accessible.

Not checked, on purpose:

- `sealed` is not required. Sealing the plugin class is good practice, not a load-time condition.
- The attribute on a struct, interface or enum: the compiler already rejects it (CS0592).
- Classes in generated code (files marked `<auto-generated/>`, `*.g.cs`, `[GeneratedCode]`).
- Projects in which `CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute` or `CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin`
  cannot be resolved: the analyzer registers nothing there.
- Projects that switch the generated entry point off (`<CheatEngineSdkGenerateEntryPoint>false</CheatEngineSdkGenerateEntryPoint>`): the
  rule states what the generated entry point needs. See When to suppress.

## Example

```csharp
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;

namespace MyPlugin;

[CheatEnginePlugin("Demo")]
public abstract class DemoPlugin : CheatEnginePlugin   // CESDK0001 twice: abstract, hidden constructor
{
    private DemoPlugin() { }
}
```

Compliant:

```csharp
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;

namespace MyPlugin;

[CheatEnginePlugin("Demo")]
public sealed class DemoPlugin : CheatEnginePlugin
{
    protected override void OnEnable() { }
    protected override void OnDisable() { }
}
```

## Code fixes

| Problem                                | Action                                      | Notes                                                                                                                                                                                                                                                                                                                                                                       |
|----------------------------------------|---------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Abstract`, `Static`                   | Replace 'abstract' / 'static' with 'sealed' | Applied to every part of a partial class that carries the modifier, in whichever document it is. Abstract members, or the missing base class of a former static class, are then yours to resolve: the compiler and this rule point at them.                                                                                                                                 |
| `MissingParameterlessConstructor`      | Add public parameterless constructor        | Inserted in front of the first declared constructor of the attributed part, else after its last field, else as first member. Not offered when the new constructor would need arguments only you know: the class has a primary constructor (every other constructor must chain to it), or its base class has no accessible constructor that can be called without arguments. |
| `InaccessibleParameterlessConstructor` | Make parameterless constructor public       | Replaces the accessibility keywords, or adds `public` when there is none. Comments next to a dropped second keyword (`private /* why */ protected`) are kept.                                                                                                                                                                                                               |

The remaining problems are design decisions and have no fix. All actions support Fix All.

## When to suppress

Do not suppress it in a plugin project: the diagnostic means the plugin will not load.

If you construct the plugin yourself and have switched the generated entry point off
(`<CheatEngineSdkGenerateEntryPoint>false</CheatEngineSdkGenerateEntryPoint>`), there is nothing to suppress: the rule
and [CESDK0002](CESDK0002.md) are not reported, because the conditions are then yours to define. The direct package
props make the property compiler-visible (`<CompilerVisibleProperty Include="CheatEngineSdkGenerateEntryPoint" />`). A
project that consumes the analyzers without those props explicitly sets it to `true` when it wants generated-bootstrap
diagnostics; otherwise generation-related rules stay silent. As a last resort, it can disable the rule for the project:

```ini
[*.cs]
dotnet_diagnostic.CESDK0001.severity = none
```
