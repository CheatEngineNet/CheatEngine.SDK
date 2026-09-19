# CESDK2003: [LuaFunction] method cannot be exported by a generated thunk

|                    |                                                                                                                                                                             |
|--------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Category           | `CESDK.Generation`                                                                                                                                                          |
| Default severity   | Error                                                                                                                                                                       |
| Enabled by default | Yes                                                                                                                                                                         |
| Code fix           | No                                                                                                                                                                          |
| Reported           | In build and full-solution analysis. The descriptor carries the `CompilationEnd` tag because `DuplicateName` needs every member of the type. The tag defers the whole rule. |

## Cause

A method carries `[CESDK.Annotations.Lua.LuaFunction("...")]`, but the LuaBindings generator
(`CESDK.SourceGenerators.LuaBindings`) cannot wrap it in a `lua_CFunction` thunk.

## Why

The generator turns a `[LuaFunction]` method into a static `[UnmanagedCallersOnly]` thunk that reads its arguments off
the Lua stack, calls the method and pushes the result. That only works for a method whose shape the thunk can bind. When
a method does not qualify, the generator emits no thunk for it and no entry in the registration table, and Cheat
Engine's Lua environment never sees the function.

## What is checked

The rule classifies the method and validates the attribute's name argument, exactly as the generator does. One
diagnostic is reported per independent problem, on the method's own location:

| Problem                    | Requirement                                                                                                                                                                                                                                   |
|----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `NotOrdinaryMethod`        | An ordinary method: not an accessor, operator, conversion, local function or explicit interface implementation.                                                                                                                               |
| `NotStatic`                | `static`: the generated thunk has no receiver to call it on.                                                                                                                                                                                  |
| `Generic`                  | No type parameters.                                                                                                                                                                                                                           |
| `Async`                    | Not `async`. The thunk would return before the continuation runs and could not catch what it throws.                                                                                                                                          |
| `InvalidName`              | The attribute's name argument is a Lua identifier (ASCII letters, digits, underscore, not starting with a digit) that is not a Lua 5.3 reserved word.                                                                                         |
| `ByRefParameter`           | Every parameter is passed by value: no `ref`, `in`, `out` or `ref readonly`.                                                                                                                                                                  |
| `ParamsParameter`          | No `params` parameter: variadic exports are not supported.                                                                                                                                                                                    |
| `OptionalParameter`        | No parameter has a default value: the thunk checks the exact argument count.                                                                                                                                                                  |
| `StateParameterNotFirst`   | A `CESDK.Lua.State.LuaState` parameter, if any, is the first parameter.                                                                                                                                                                       |
| `UnsupportedParameterType` | Every parameter is `int`, `long`, `float`, `double`, `bool`, `nuint`, `ReadOnlySpan<byte>`, `string` or `CESDK.Lua.State.LuaState` (first only).                                                                                              |
| `UnsupportedReturnType`    | The return type is `void` or one of the same marshalled kinds.                                                                                                                                                                                |
| `DuplicateName`            | No other `[LuaFunction]` of the same containing type registers the same Lua name. Only members with no other problem count. The generator drops every member of a duplicated name from the registration table, and this rule names the cause. |

## Example

```csharp
using CESDK.Annotations.Lua;

namespace MyPlugin;

public static partial class Functions
{
    [LuaFunction("end")]
    public static long Add(long a, long b, int c = 0) =>   // CESDK2003 twice: reserved name, default value
        a + b + c;
}
```

Two independent problems on the same method: the name `"end"` is a Lua 5.3 reserved word, and `c` has a default value.
Fixing one does not silence the other. Both are reported.

`DuplicateName` needs two otherwise valid members:

```csharp
using CESDK.Annotations.Lua;

namespace MyPlugin;

public static partial class Functions
{
    [LuaFunction("shared")]
    public static int First() => 1;    // CESDK2003: shares its Lua name with Second

    [LuaFunction("shared")]
    public static int Second() => 2;   // CESDK2003: shares its Lua name with First
}
```

Both methods are individually valid, so without this check the generator would silently drop both from the registration
table, and neither `First` nor `Second` would exist in Lua.

## When to suppress

Do not suppress it in a plugin project: the diagnostic means the function silently does not exist in Lua. Reshape the
method, or drop the attribute if it was not meant to be exported.
