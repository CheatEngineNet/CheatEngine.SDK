# CESDK0004: Plugin assembly declares a namespace under 'CESDK'

|                    |                                                                       |
|--------------------|-----------------------------------------------------------------------|
| Category           | `CheatEngine.SDK.Plugin`                                              |
| Default severity   | Warning                                                               |
| Enabled by default | Yes                                                                   |
| Code fix           | No (renaming a namespace is a refactoring: use the IDE's rename)      |
| Reported           | In build and full-solution analysis only (compilation-end diagnostic) |

## Cause

A project that contains a `[CheatEnginePlugin]` class declares the namespace `CESDK` or a namespace nested under it, for
example `CESDK.MyPlugin` or `CESDK.Tools.Deep`.

## Why

Cheat Engine finds a managed plugin by a hard-coded type name: it asks the .NET host for the method `CEPluginInitialize`
of the type `CESDK.CESDK`, and that type has to be in the plugin assembly itself. The SDK generates it for you, so every
plugin assembly contains a class named `CESDK` in the namespace `CESDK`.

C# resolves a simple name by walking outwards through the enclosing namespaces before it reaches the global namespace.
For code inside `namespace CESDK.MyPlugin`, the name `CESDK` is looked up in `CESDK.MyPlugin`, then in `CESDK`, where it
finds the generated class and stops. From there on `CESDK.Something` means "member `Something` of the class
`CESDK.CESDK`", never "namespace `Something` under the namespace `CESDK`". A qualified name that starts with `CESDK.`
therefore no longer resolves to a namespace that you declared under `CESDK` yourself:

```csharp
namespace CESDK.Tools                                            // your own namespace under CESDK
{
    public sealed class Helper { }
}

namespace CESDK.MyPlugin
{
    using CESDK.Tools;                                           // error CS0426: the type name 'Tools' does not exist in the type 'CESDK'

    public sealed class Settings
    {
        private readonly CESDK.Tools.Helper _helper = new();     // error CS0426, same reason
    }
}
```

`global::CESDK.Tools.Helper` and `using` directives placed above the namespace declaration keep working. That makes the
failure look random: it depends on where a `using` sits and on whether a name happens to be written with the `CESDK.`
prefix. The SDK itself is not affected: its namespaces start with `CheatEngine.SDK`, so `CheatEngine.SDK.Hosting.Plugin`
resolves inside a namespace under `CESDK` like anywhere else. Plugin code does not belong under `CESDK`, which is the
namespace of the type Cheat Engine requires.

## What is checked

- Only projects with at least one class marked `[CheatEnginePlugin]`. The SDK's own libraries and ordinary class
  libraries are not plugin assemblies and are left alone.
- Every outermost namespace declaration (block or file-scoped) whose first name segment is exactly `CESDK`,
  case-sensitive. One diagnostic per declaration, located on the declared name. Declarations nested inside it are
  covered by it.
- Not flagged: `CESDKPlugin`, `Cesdk.Tools`, `MyCompany.CESDK`, and the SDK's own namespaces under `CheatEngine.SDK`.
- Generated code is ignored, so the generated `namespace CESDK { class CESDK }` itself never triggers the rule.
- The rule stays on when the generated entry point is switched off (`CheatEngineSdkGenerateEntryPoint=false`): a hand-written
  bootstrap has to provide the same `CESDK.CESDK` type.

The rule needs to know whether the compilation contains a plugin class, so it runs in the compilation-end phase. It
appears in build output and in full-solution analysis where an IDE offers it, not while typing.

## How to fix

Use a root namespace that does not start with `CESDK`: set `<RootNamespace>` in the project file and rename the declared
namespaces.

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

## When to suppress

If a namespace under `CESDK` has to stay (for example to keep type names stable for existing Lua scripts or serialized
data), the code inside it must never use the simple name `CESDK`. Write `global::CESDK.` for a name that starts with
`CESDK.`, and keep `using` directives that name a namespace under `CESDK` above the namespace declaration. Then the
warning can be suppressed for that declaration.
