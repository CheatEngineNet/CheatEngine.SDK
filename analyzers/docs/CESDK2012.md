# CESDK2012: Type impersonates an SDK Lua contract type

|                    |                              |
|--------------------|------------------------------|
| Category           | `CheatEngine.SDK.Generation` |
| Default severity   | Error                        |
| Enabled by default | Yes                          |
| Code fix           | No                           |
| Reported           | While typing and in build    |

## Cause

A `[LuaGlobal]` or `[LuaFunction]` method uses a type named `CheatEngine.SDK.Lua.Marshalling.LuaOptional<T>` or
`CheatEngine.SDK.Lua.Calls.LuaOperationStatus` that is not defined by the `CheatEngine.SDK.Lua` assembly: a type
declared in the project's own source, or in another referenced assembly.

## Why

The generators select the optional argument or result shape, and the `LuaOperationStatus` Outcome form, from these two
types. They recognise them by symbol identity: the type the `CheatEngine.SDK.Lua` assembly defines, found among every
type with that metadata name. A namespace and a name are only a lookup key. Accepting a same-named type would let the
generated body return or read a different type than the declaration names, or silently turn an Outcome declaration into
something else. The generator emits nothing for such a declaration and this rule names the reason, instead of a generic
"unsupported type" message.

## What is checked

Every argument, result and return type whose namespace, name and arity equal `LuaOptional`1` or `LuaOperationStatus` must
be the `CheatEngine.SDK.Lua` type. When the project does not reference `CheatEngine.SDK.Lua` at all, every such type is a
look-alike.

## Example

```csharp
namespace CheatEngine.SDK.Lua.Calls
{
    public readonly struct LuaOperationStatus { }   // a source copy of the SDK type
}

namespace MyPlugin
{
    using CheatEngine.SDK.Annotations.Lua;
    using CheatEngine.SDK.Lua.Calls;

    public static partial class Memory
    {
        [LuaGlobal("readInteger")]
        public static partial LuaOperationStatus TryReadInt32(nuint address, out int value);   // CESDK2012
    }
}
```

Compliant: remove the source copy and reference the `CheatEngine.SDK` package, which brings the real
`CheatEngine.SDK.Lua` assembly.

## When to suppress

Do not suppress it: the diagnostic means the binding has no generated code and names a type the SDK does not define.
