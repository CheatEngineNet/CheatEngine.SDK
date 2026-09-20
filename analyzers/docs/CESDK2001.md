# CESDK2001: Lua binding needs AllowUnsafeBlocks

|                    |                              |
|--------------------|------------------------------|
| Category           | `CheatEngine.SDK.Generation` |
| Default severity   | Error                        |
| Enabled by default | Yes                          |
| Code fix           | No                           |
| Reported           | While typing and in build    |

## Cause

A method carries `[CheatEngine.SDK.Annotations.Lua.LuaFunction("...")]`, but the project that declares it does not
compile with `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.

## Why

The LuaBindings generator (`CheatEngine.SDK.SourceGenerators.LuaBindings`) emits, for `[LuaFunction]`, an
`[UnmanagedCallersOnly]`
thunk and a registration table that takes the address of that thunk. That needs unsafe code. The generator reads
`CSharpCompilationOptions.AllowUnsafe` once per compilation. When it is off, the generator emits no function file.
Without this rule the only symptom would be a plugin whose Lua functions silently do not exist at run time.

The package deliberately does not change `AllowUnsafeBlocks`: a project that contains only `[LuaGlobal]`,
`[LuaClass]`, `[LuaMethod]` or `[LuaProperty]` bindings stays safe by default. A project that declares a
`[LuaFunction]` must opt in itself. Otherwise the generator emits no function file and this diagnostic reports every
`[LuaFunction]` target.

## What is checked

The rule reports one diagnostic per attributed method, at the method's own location, whatever other problems the method
has. CESDK2002 through CESDK2004 explain those independently, so one method can be reported by several rules. The rule
does not check whether the attribute application itself is well formed: a missing or mistyped attribute argument is
already a compiler error. In projects that do not reference `CheatEngine.SDK.Annotations` the analyzer registers
nothing.

## Example

The project file sets:

```xml
<AllowUnsafeBlocks>false</AllowUnsafeBlocks>
```

```csharp
using CheatEngine.SDK.Annotations.Lua;

namespace MyPlugin;

public static partial class Functions
{
    [LuaFunction("add")]
    public static long Add(long a, long b) => a + b;   // CESDK2001
}
```

Compliant: set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the project file.

## When to suppress

Do not suppress it in a plugin project: the diagnostic means every Lua binding of the project silently does not exist.
Suppress it only in a project that keeps the attributes purely for documentation or reflection and never enables code
generation for them:

```ini
[*.cs]
dotnet_diagnostic.CESDK2001.severity = none
```
