# CESDK2013: LuaOptional is not supported in this position

|                    |                              |
|--------------------|------------------------------|
| Category           | `CheatEngine.SDK.Generation` |
| Default severity   | Error                        |
| Enabled by default | Yes                          |
| Code fix           | No                           |
| Reported           | While typing and in build    |

## Cause

A binding uses `CheatEngine.SDK.Lua.Marshalling.LuaOptional<T>` where the generators do not support it, or with a type
argument they cannot marshal.

## Why

`LuaOptional<T>` keeps three states apart: omitted (no value at all), an explicit Lua `nil`, and a value. The generators
support it where each state has a defined Lua meaning:

- a `[LuaGlobal]` argument (omitted is not pushed, `Nil` is pushed as `nil`) and a `[LuaFunction]` parameter (an absent
  position is omitted);
- a `[LuaGlobal]` `out` result (a position the global did not return is omitted).

A return value has no such meaning: the throwing form returns exactly one value or throws, and a thunk returns one value
or none. `[LuaMethod]` and `[LuaProperty]` members do not support optional values yet. The type argument must be a
built-in marshalled kind: `int`, `long`, `float`, `double`, `bool`, `nuint` or `string`. `string?` is refused because
`nil` is the `Nil` state, never a `null` string; a custom-marshalled or nested type, or an explicit `[LuaMarshaller]` on
the parameter, is refused because the optional states would have no defined meaning for its marshaller. The generator
emits nothing for the member.

## What is checked

| Position                                          | Supported                                   |
|---------------------------------------------------|---------------------------------------------|
| `[LuaGlobal]` argument, `[LuaFunction]` parameter | Yes, in a trailing run (see CESDK2010)      |
| `[LuaGlobal]` `out` result                        | Yes, after the required results (CESDK2011) |
| `[LuaGlobal]` throwing-form return                | No                                          |
| `[LuaFunction]` return                            | No                                          |
| `[LuaMethod]` parameter, result or return         | Not yet                                     |
| `[LuaProperty]` type                              | Not yet                                     |

`T` must be `int`, `long`, `float`, `double`, `bool`, `nuint` or `string`, and the parameter carries no `[LuaMarshaller]`.

## Example

```csharp
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Marshalling;

namespace MyPlugin;

public static partial class Symbols
{
    [LuaGlobal("getAddressSafe")]
    public static partial LuaOptional<nuint> ResolveAddress(string expression);   // CESDK2013: optional return
}
```

Compliant: an `out LuaOptional<T>` result of the Try or Outcome form.

```csharp
public static partial class Symbols
{
    [LuaGlobal("getAddressSafe")]
    public static partial bool TryResolveAddress(string expression, out LuaOptional<nuint> address);
}
```

## When to suppress

Do not suppress it: the diagnostic means the member has no generated code.
