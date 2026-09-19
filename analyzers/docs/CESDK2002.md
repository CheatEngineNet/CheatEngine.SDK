# CESDK2002: Type cannot receive a generated Lua binding part

|                    |                           |
|--------------------|---------------------------|
| Category           | `CESDK.Generation`        |
| Default severity   | Error                     |
| Enabled by default | Yes                       |
| Code fix           | No                        |
| Reported           | While typing and in build |

## Cause

A method carries `[LuaFunction]` or `[LuaGlobal]`, but the type that declares it, or a type that type is nested in,
cannot receive a generated part.

## Why

The LuaBindings generator (`CESDK.SourceGenerators.LuaBindings`) adds a generated `partial` part to the containing type
of every bound member. The part holds the thunk and registration table for `[LuaFunction]`, or the implementing
declaration for `[LuaGlobal]`.

A second part can only be added when every type in the chain, from the declaring type outward, qualifies. Each type must
be `partial` and a class or a struct (interfaces, enums and delegates take no generated members). It must have no type
parameters at any level and must not be a `file`-local type. When any of this does not hold, the generator emits nothing
for every member of that type.

## What is checked

The rule walks the declaring type and every type it is nested in. It reports one diagnostic per independent problem
found anywhere in that chain, on the member's own location. A type with several bound members gets one diagnostic per
member, each on its own declaration.

| Problem            | Requirement                                                                     |
|--------------------|---------------------------------------------------------------------------------|
| `NotClassOrStruct` | The declaring type, and every type it is nested in, is a `class` or a `struct`. |
| `Generic`          | Neither the declaring type nor a type it is nested in has type parameters.      |
| `FileLocal`        | Neither the declaring type nor a type it is nested in is a `file` type.         |
| `NotPartial`       | The declaring type, and every type it is nested in, is declared `partial`.      |

This rule is about the container. [CESDK2003](CESDK2003.md) and [CESDK2004](CESDK2004.md) are about the member itself
(its own signature, name and modifiers) and fire independently of it.

## Example

```csharp
using CESDK.Annotations.Lua;

namespace MyPlugin;

public static class Functions
{
    [LuaFunction("add")]
    public static long Add(long a, long b) => a + b;   // CESDK2002: Functions must be declared partial
}
```

Compliant:

```csharp
using CESDK.Annotations.Lua;

namespace MyPlugin;

public static partial class Functions
{
    [LuaFunction("add")]
    public static long Add(long a, long b) => a + b;
}
```

## When to suppress

Do not suppress it in a plugin project: the diagnostic means the binding silently does not exist. Make the containing
type, and every type it is nested in, `partial`, non-generic and not `file`-local instead.
