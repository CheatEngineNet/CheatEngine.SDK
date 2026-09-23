# CESDK2010: Optional Lua argument is not in a trailing run

|                    |                              |
|--------------------|------------------------------|
| Category           | `CheatEngine.SDK.Generation` |
| Default severity   | Error                        |
| Enabled by default | Yes                          |
| Code fix           | No                           |
| Reported           | While typing and in build    |

## Cause

A `[LuaGlobal]` method or a `[LuaFunction]` method declares a
`CheatEngine.SDK.Lua.Marshalling.LuaOptional<T>` argument followed by a required argument.

## Why

`LuaOptional<T>` states that an argument can be omitted: a `[LuaGlobal]` wrapper does not push an omitted argument, and
a `[LuaFunction]` thunk reads a position the caller did not pass as omitted. Lua arguments are positional, so only a
trailing argument can be absent. An omitted argument followed by a present one cannot be expressed: Lua would receive the
later value in the earlier position. Cheat Engine functions can behave differently for an omitted argument and for an
explicit `nil`, so the generator never pushes `nil` in place of an omitted argument.

The generator emits nothing for the declaration.

## What is checked

Arguments are read left to right, after an optional leading `LuaState`. Once a `LuaOptional<T>` argument has been seen,
every later argument must also be a `LuaOptional<T>`. Results (`out` parameters and span pairs) are not arguments and may
follow the optional run.

## Example

```csharp
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Marshalling;

namespace MyPlugin;

public static partial class Tables
{
    [LuaGlobal("loadTable")]
    public static partial void LoadTable(LuaOptional<bool> merge, string path);   // CESDK2010
}
```

Compliant: the required argument first, the optional one last.

```csharp
public static partial class Tables
{
    [LuaGlobal("loadTable")]
    public static partial void LoadTable(string path, LuaOptional<bool> merge);
}
```

A caller writes `default` (or `LuaOptional.Omitted<bool>()`) to omit `merge`, `LuaOptional.Nil<bool>()` to pass `nil`,
and `LuaOptional.Of(true)` to pass a value. When several optional arguments are declared, omitting one while passing a
later one throws `ArgumentException` before the wrapper touches Lua.

## When to suppress

Do not suppress it: the diagnostic means the binding has no generated body or thunk. Reorder the parameters.
