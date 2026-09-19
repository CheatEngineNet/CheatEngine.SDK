<div align="center">

# 11 · Diagnostics

**Every CESDK rule, the code that trips it, and the fix.**

**Level** `Beginner` · **Time** `20 min` · **Needs** `Guide 02`

[Examples index](../README.md) · [Previous: Logging and errors](../10-logging-and-errors/README.md) · [Next: Trainer](../12-trainer/README.md)

</div>

---

|               |                                                                                             |
|---------------|---------------------------------------------------------------------------------------------|
| **You build** | Nothing new: you break a plugin on purpose, read the message, and fix it                    |
| **You learn** | What each rule says, why it exists, how to fix it, and how to set its severity              |
| **You need**  | A plugin project like the one in [01 · Your first plugin](../01-first-plugin/README.md)     |
| **Covers**    | `CESDK0001`, `CESDK0002`, `CESDK0004`, `CESDK1004`, `CESDK2001` to `CESDK2004`, `CESDK9101` |

## Objective

Recognize every message the CESDK analyzers and build checks can show, and fix its cause in one edit.

## Why it matters

Cheat Engine tells you nothing when it refuses a plugin, and a Lua function that was never generated simply does not
exist. Each rule turns one of those silent failures into a message in the editor or the build, with a page that
explains the cause.

## Find the rule fast

| What you see                                                         | Rule                                                                                               |
|----------------------------------------------------------------------|----------------------------------------------------------------------------------------------------|
| Cheat Engine refuses the DLL, or the plugin is missing from the list | `CESDK0001` or `CESDK0002`                                                                         |
| A Lua global does not exist after the plugin is enabled              | `CESDK2001`, `CESDK2002` or `CESDK2003`                                                            |
| A `[LuaGlobal]` method reports `CS8795`, a missing implementation    | `CESDK2004`, `CESDK2001` or `CESDK2002`: each one stops the generator from writing the body        |
| `CS0426` on a name that starts with `CESDK.`                         | `CESDK0004`                                                                                        |
| A hand written native callback that can end Cheat Engine             | `CESDK1004`                                                                                        |
| The build stops on `PlatformTarget=x86`                              | `CESDK9101`                                                                                        |
| `CS9057` in the build                                                | Not a CESDK rule: the .NET SDK is older than 10.0.401, so the analyzers and generators do not load |

| Rule                                             | Title                                                           | Severity | Code fix                   |
|--------------------------------------------------|-----------------------------------------------------------------|----------|----------------------------|
| [`CESDK0001`](../../analyzers/docs/CESDK0001.md) | Plugin class cannot be constructed by the generated entry point | Error    | Three actions              |
| [`CESDK0002`](../../analyzers/docs/CESDK0002.md) | More than one plugin class in the assembly                      | Error    | None                       |
| [`CESDK0004`](../../analyzers/docs/CESDK0004.md) | Plugin assembly declares a namespace under `CESDK`              | Warning  | None                       |
| [`CESDK1004`](../../analyzers/docs/CESDK1004.md) | Exception can escape an `[UnmanagedCallersOnly]` method         | Warning  | Wrap the body in try/catch |
| [`CESDK2001`](../../analyzers/docs/CESDK2001.md) | Lua binding needs `AllowUnsafeBlocks`                           | Error    | None                       |
| [`CESDK2002`](../../analyzers/docs/CESDK2002.md) | Type cannot receive a generated Lua binding part                | Error    | None                       |
| [`CESDK2003`](../../analyzers/docs/CESDK2003.md) | `[LuaFunction]` method cannot be exported by a generated thunk  | Error    | None                       |
| [`CESDK2004`](../../analyzers/docs/CESDK2004.md) | `[LuaGlobal]` method cannot receive a generated body            | Error    | None                       |

`CESDK0xxx` covers the plugin shape, `CESDK1xxx` covers runtime safety and `CESDK2xxx` covers the input of the
generators. Identifiers are never renumbered or reused.

## The gallery

Each entry shows the code that trips the rule, the message you get, and the code that fixes it.

### CESDK0001 · The plugin class cannot be created

Cheat Engine calls one fixed entry point. The generated code creates your plugin with `new TrainerPlugin()`, so the
class needs a constructor with no required arguments.

<!-- expect: CESDK0001 -->

```csharp
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;

namespace Broken0001;

[CheatEnginePlugin("Trainer")]
public sealed class TrainerPlugin(string profile) : CheatEnginePlugin
{
    protected override void OnEnable() => Console.WriteLine(profile);

    protected override void OnDisable() { }
}
```

```text
error CESDK0001: Plugin class 'TrainerPlugin' must declare a public or internal constructor callable with no
arguments (parameterless, or with only optional/'params' parameters): the generated entry point calls it as 'new T()'
```

<!-- alone -->

```csharp
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;

namespace Fixed0001;

[CheatEnginePlugin("Trainer")]
public sealed class TrainerPlugin : CheatEnginePlugin
{
    private string _profile = "default";

    protected override void OnEnable() => Console.WriteLine(_profile);

    protected override void OnDisable() { }
}
```

The same rule reports an `abstract`, `static` or generic class, a private constructor, a `file` type, a class that does
not derive from `CheatEnginePlugin`, `required` members, and an empty display name. The light bulb offers three fixes:
replace `abstract` or `static` with `sealed`, add a public parameterless constructor, and make the parameterless
constructor public. Never suppress this rule in a plugin project.

### CESDK0002 · Two plugins in one assembly

One DLL is one plugin: Cheat Engine calls a single entry point per assembly, and the generator will not guess between
two classes.

<!-- expect: CESDK0002 -->

```csharp
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;

namespace Broken0002;

[CheatEnginePlugin("Scanner")]
public sealed class ScannerPlugin : CheatEnginePlugin
{
    protected override void OnEnable() { }

    protected override void OnDisable() { }
}

[CheatEnginePlugin("Trainer")]
public sealed class TrainerPlugin : CheatEnginePlugin
{
    protected override void OnEnable() { }

    protected override void OnDisable() { }
}
```

```text
error CESDK0002: 'TrainerPlugin' is one of 2 classes marked [CheatEnginePlugin]; a plugin assembly must contain
exactly one, because Cheat Engine calls a single entry point per assembly
```

The fix is structural: one plugin, one project. Code that both plugins need goes into a class library that they
reference.

```text
MyPlugins.slnx
    ScannerPlugin/    one [CheatEnginePlugin("Scanner")] class
    TrainerPlugin/    one [CheatEnginePlugin("Trainer")] class
    Shared/           a class library that both reference
```

### CESDK0004 · A namespace under `CESDK`

The generated entry point is a class named `CESDK` in the namespace `CESDK`. Inside `namespace CESDK.Trainer`, the
simple name `CESDK` means that class, so the SDK namespaces stop resolving.

<!-- expect: CESDK0004 -->

```csharp
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;

namespace CESDK.Trainer;

[CheatEnginePlugin("Trainer")]
public sealed class TrainerPlugin : CheatEnginePlugin
{
    protected override void OnEnable() { }

    protected override void OnDisable() { }
}
```

```text
warning CESDK0004: Namespace 'CESDK.Trainer' is 'CESDK' or nested under it in a plugin assembly. Cheat Engine forces
a type named 'CESDK.CESDK' into every plugin assembly, so inside that namespace the simple name 'CESDK' binds to the
type instead of the SDK namespaces. Use a different root namespace.
```

`using` directives above the namespace still work, which makes this warning easy to ignore until a fully qualified name
appears inside it:

<!-- expect: CS0426 -->

```csharp
using CESDK.Annotations.Plugin;

namespace CESDK.Trainer;

[CheatEnginePlugin("Trainer")]
public sealed class TrainerPlugin : CESDK.Hosting.Plugin.CheatEnginePlugin
{
    protected override void OnEnable() { }

    protected override void OnDisable() { }
}
```

```text
error CS0426: The type name 'Hosting' does not exist in the type 'CESDK'
```

Use a root namespace that does not start with `CESDK`, and set `<RootNamespace>` in the project file to match.

<!-- alone -->

```csharp
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;

namespace Trainer;

[CheatEnginePlugin("Trainer")]
public sealed class TrainerPlugin : CheatEnginePlugin
{
    protected override void OnEnable() { }

    protected override void OnDisable() { }
}
```

> [!NOTE]
> A compiler error in a declaration hides analyzer results in a command line build. The second snippet reports
> `CS0426` and no `CESDK0004`, because the analyzers do not run until the declarations compile. Fix the compiler errors
> first.

### CESDK1004 · An exception can escape a native callback

Native code calls an `[UnmanagedCallersOnly]` method directly. No managed frame sits above it, so an exception ends the
process, and the process is Cheat Engine with your session in it. The generated thunks already guard themselves. The
rule is for the callbacks you write by hand (see [08 · Running Lua](../08-running-lua/README.md)).

<!-- expect: CESDK1004 -->

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Broken1004;

internal static class Callbacks
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnLuaCall(nint luaState)
    {
        return DoWork(luaState);
    }

    private static int DoWork(nint luaState) => 1;
}
```

```text
warning CESDK1004: An exception can escape 'OnLuaCall', which native code calls directly: the whole body must be one
try statement whose catch-all clause returns a failure value
```

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fixed1004;

internal static class Callbacks
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnLuaCall(nint luaState)
    {
        try
        {
            return DoWork(luaState);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int DoWork(nint luaState) => 1;
}
```

The light bulb applies exactly this shape. Check that the failure value fits the callback: `0` means `FALSE` for Cheat
Engine callbacks and "no results" for a Lua function. Keep the `catch` block trivial, because the rule cannot prove that
a call inside it does not throw.

### CESDK2001 · Lua bindings without unsafe code

The generator takes the address of each thunk, and that needs unsafe code. Without it the generator emits nothing for
any `[LuaFunction]` or `[LuaGlobal]`, and the functions silently do not exist.

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
</PropertyGroup>
```

```text
error CESDK2001: 'Add' is a Lua binding, but this compilation does not allow unsafe code (AllowUnsafeBlocks); the
generator emits nothing for any [LuaFunction] or [LuaGlobal] member until it is enabled
```

The `CESDK` package sets `AllowUnsafeBlocks` to `true` while your project leaves it unset, so the fix is to remove your
`false`.

### CESDK2002 · A type that cannot receive generated code

The generator adds a second `partial` part to the type that holds your binding, so that type, and every type around it,
must be a non generic, non `file` class or struct declared `partial`.

<!-- expect: CESDK2002 -->

```csharp
using CESDK.Annotations.Lua;

namespace Broken2002;

public static class Functions
{
    [LuaFunction("my_plugin_add")]
    public static long Add(long a, long b) => a + b;
}
```

```text
error CESDK2002: 'Add' is a Lua binding, but its containing type must be declared partial, like every type it is nested
in: a generated part needs a second declaration to add itself to
```

```csharp
using CESDK.Annotations.Lua;

namespace Fixed2002;

public static partial class Functions
{
    [LuaFunction("my_plugin_add")]
    public static long Add(long a, long b) => a + b;
}
```

### CESDK2003 · A method that cannot become a Lua function

The thunk reads a fixed number of typed arguments, so the method needs a shape it can bind. One method can break several
rules at once, and each one is reported.

<!-- expect: CESDK2003 -->

```csharp
using CESDK.Annotations.Lua;

namespace Broken2003;

public static partial class Functions
{
    [LuaFunction("end")]
    public static long Add(long a, long b, int bonus = 0) => a + b + bonus;
}
```

```text
error CESDK2003: Lua function 'Add' must be given a Lua identifier in [LuaFunction] that is not a reserved word
error CESDK2003: Lua function 'Add' must not have a parameter with a default value: the thunk checks the exact
argument count
```

Two methods with the same Lua name are both dropped from the registration table, so neither exists in Lua:

<!-- expect: CESDK2003 -->

```csharp
using CESDK.Annotations.Lua;

namespace Broken2003Duplicate;

public static partial class Functions
{
    [LuaFunction("shared")]
    public static int First() => 1;

    [LuaFunction("shared")]
    public static int Second() => 2;
}
```

```csharp
using CESDK.Annotations.Lua;

namespace Fixed2003;

public static partial class Functions
{
    [LuaFunction("my_plugin_add")]
    public static long Add(long a, long b) => a + b;

    [LuaFunction("my_plugin_add_bonus")]
    public static long AddBonus(long a, long b, int bonus) => a + b + bonus;
}
```

The other checks are `static`, non generic, not `async`, no `ref`, `in` or `out` parameters, no `params`, a leading
`LuaState` only in first position, and parameter and return types from the
[type map](../02-lua-functions/README.md#the-type-map).

### CESDK2004 · A binding without a body

A `[LuaGlobal]` method is the declaration half of a `partial` method. The generator writes the other half only when the
shape is valid: arguments first, `out` results last, and a `bool` return for the Try form.

<!-- expect: CS8795 -->

```csharp
using CESDK.Annotations.Lua;

namespace Broken2004;

internal static partial class Memory
{
    [LuaGlobal("readInteger")]
    public static partial int TryReadInt32(nuint address, out int value);
}
```

```text
error CS8795: Partial method 'Memory.TryReadInt32(nuint, out int)' must have an implementation part because it has
accessibility modifiers.
```

The compiler reports the missing body because the generator wrote none, and `CESDK2004` is the rule that names the
cause: a declaration with `out` results is a Try form and must return `bool`. A declaration that the compiler accepts
without a body shows the rule directly:

<!-- expect: CESDK2004 -->

```csharp
using CESDK.Annotations.Lua;

namespace Broken2004Shape;

internal static partial class Dialogs
{
    [LuaGlobal("showMessage")]
    static partial void ShowMessage(string text, object details);
}
```

```text
error CESDK2004: Lua global binding 'ShowMessage' must use only argument types a marshaller pushes: int, long, float,
double, bool, nuint, ReadOnlySpan<byte> or string
```

```csharp
using CESDK.Annotations.Lua;

namespace Fixed2004;

internal static partial class Memory
{
    [LuaGlobal("readInteger")]
    public static partial bool TryReadInt32(nuint address, bool signed, out int value);

    [LuaGlobal("readInteger")]
    public static partial int ReadInt32(nuint address, bool signed);
}
```

Both declarations bind `readInteger`: a Try form and a throwing form (see
[03 · Calling Cheat Engine](../03-calling-cheat-engine/README.md)). Pass `true` for a signed 32-bit result.

### CESDK9101 · An x86 plugin

Cheat Engine hosts plugins in an x64 process, so the package stops the build when the project targets x86.

```xml
<PropertyGroup>
  <PlatformTarget>x86</PlatformTarget>
</PropertyGroup>
```

```text
error CESDK9101: MyPlugin targets PlatformTarget=x86. Cheat Engine hosts plugins in an x64 process only: set
<PlatformTarget>x64</PlatformTarget> (or <Platforms>x64</Platforms>) instead.
```

## Where and when you see them

| Rule                                                            | Reported                                                                                                                               |
|-----------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------|
| `CESDK0001`, `CESDK1004`, `CESDK2001`, `CESDK2002`, `CESDK2004` | While you type and in the build                                                                                                        |
| `CESDK0002`, `CESDK0004`, `CESDK2003`                           | In the build and in full solution analysis only. They need the whole project, for example to count plugin classes or compare Lua names |

## Set a severity

Configure a rule like any other analyzer diagnostic. Raise a warning to an error in an `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.CESDK1004.severity = error
```

Do not switch off a rule that reports a plugin shape or a generator input in a plugin project. It means the plugin does
not load or a Lua function does not exist. The one reasoned exception is `CESDK0004`: a namespace under `CESDK` may stay
when no code inside it uses the simple name `CESDK` and every `using` sits above the namespace.

## Project switches the package reads

| Property                  | Default                                         | Effect                                                                                                                                  |
|---------------------------|-------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------|
| `CesdkGenerateEntryPoint` | `true`                                          | Set it to `false` to write `CESDK.CESDK` by hand. `CESDK0001` and `CESDK0002` are then not reported, because you define the entry point |
| `AllowUnsafeBlocks`       | `true`, only while your project leaves it unset | The Lua binding generator needs it (`CESDK2001`)                                                                                        |
| `EnableDynamicLoading`    | `true`, only while your project leaves it unset | Copies the referenced assemblies next to the plugin and writes its `.runtimeconfig.json`                                                |
| `PlatformTarget`          | Your choice                                     | `x86` fails the build with `CESDK9101`                                                                                                  |

## Promise

- Every diagnostic has a help link to its own rule page, and the [rule index](../../analyzers/docs/README.md) lists them
  all.
- A rule that reports a plugin shape or a generator input explains a plugin that would otherwise not load or a Lua
  function that would otherwise not exist.
- The generators emit nothing for input they cannot serve, so a broken binding never produces broken generated code.

## Before you move on

- [ ] Your plugin project builds with no `CESDK` diagnostic.
- [ ] You know the three rules that need a full build: `CESDK0002`, `CESDK0004` and `CESDK2003`.
- [ ] Your `.editorconfig` raises `CESDK1004` to an error if you write native callbacks by hand.

---

<div align="center">

[Examples index](../README.md) · **Next:** [12 · Trainer](../12-trainer/README.md)

</div>
