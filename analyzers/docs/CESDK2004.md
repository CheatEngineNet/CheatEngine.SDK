# CESDK2004: [LuaGlobal] method cannot receive a generated body

|                    |                              |
|--------------------|------------------------------|
| Category           | `CheatEngine.SDK.Generation` |
| Default severity   | Error                        |
| Enabled by default | Yes                          |
| Code fix           | No                           |
| Reported           | While typing and in build    |

## Cause

A method carries `[CheatEngine.SDK.Annotations.Lua.LuaGlobal("...")]`, but the LuaBindings generator
(`CheatEngine.SDK.SourceGenerators.LuaBindings`) cannot write its body.

## Why

The generator implements a `[LuaGlobal]` method as the missing half of a `partial` declaration: push the global, push
the arguments, make one protected call, read the results, restore the stack. That only works for the defining
declaration of a `static`, non-generic, non-`async` partial method that has no implementing part, with a valid Lua name.

The parameter list has a fixed shape:

```text
(LuaState state, argument, argument, ..., out result, out result, ...)
```

The leading `LuaState` is optional. The arguments are by-value parameters of a marshalled kind. A result is an `out`
parameter of a marshalled kind other than `ReadOnlySpan<byte>`, or a copy-out pair
`Span<byte> destination, out int written`.

The results decide the form. Any `out` result makes it the **Try** form, which must return `bool`. No result makes it
the **throwing** form, whose return type is `void` or a marshalled kind other than `ReadOnlySpan<byte>`.

When a declaration does not qualify, the generator writes no body for it. A `partial` declaration with an accessibility
modifier needs an implementing part (CS8795), so the compiler then reports the missing implementation as well. This rule
explains why.

## What is checked

The rule classifies the method and validates the attribute's name argument. One diagnostic is reported per independent
problem, on the method's own location:

| Problem                    | Requirement                                                                                                                                                                       |
|----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `NotOrdinaryMethod`        | An ordinary method: not an accessor, operator, local function or explicit interface implementation.                                                                               |
| `NotStatic`                | `static`.                                                                                                                                                                         |
| `NotPartialDefinition`     | The defining declaration of a `partial` method (not the implementing part, not a non-partial method).                                                                             |
| `AlreadyImplemented`       | No implementing declaration exists.                                                                                                                                               |
| `Generic`                  | No type parameters.                                                                                                                                                               |
| `Async`                    | Not `async`.                                                                                                                                                                      |
| `InvalidName`              | The attribute's name argument is a Lua identifier that is not a Lua 5.3 reserved word.                                                                                            |
| `ByRefParameter`           | Arguments are passed by value (results are `out` parameters instead): no `ref`, `in` or `ref readonly`.                                                                           |
| `ParamsParameter`          | No `params` parameter.                                                                                                                                                            |
| `OptionalParameter`        | No parameter has a default value: the body pushes every argument.                                                                                                                 |
| `StateParameterNotFirst`   | A `CheatEngine.SDK.Lua.State.LuaState` parameter, if any, is the first parameter.                                                                                                 |
| `UnsupportedParameterType` | Every by-value argument is `int`, `long`, `float`, `double`, `bool`, `nuint`, `ReadOnlySpan<byte>`, `string` or the leading `LuaState`.                                           |
| `ResultBeforeArgument`     | Every argument precedes every result: `out` parameters and copy-out pairs come last.                                                                                              |
| `UnsupportedResultType`    | Every `out` result is `int`, `long`, `float`, `double`, `bool`, `nuint` or `string`, or a `Span<byte> destination, out int written` copy-out pair.                                |
| `SpanResult`               | No result is `ReadOnlySpan<byte>` (an `out` parameter or the return type): it would point into a Lua string popped before the wrapper returns. Use the copy-out pair or `string`. |
| `UnsupportedReturnType`    | The return type of the throwing form is `void` or one of the same marshalled kinds (`bool` included) other than `ReadOnlySpan<byte>`.                                             |
| `TryFormReturnNotBool`     | A declaration with `out` results returns `bool`: that is the whole shape of the Try form.                                                                                         |

## Example

```csharp
using CheatEngine.SDK.Annotations.Lua;

namespace MyPlugin;

public static partial class Memory
{
    [LuaGlobal("readInteger")]
    public static partial int TryReadInt32(nuint address, bool signed, out int value);   // CESDK2004: must return bool
}
```

Compliant: the Try form, a `bool` return with the `out` result. Dropping the `out` parameter gives the throwing form
instead.

```csharp
using CheatEngine.SDK.Annotations.Lua;

namespace MyPlugin;

public static partial class Memory
{
    [LuaGlobal("readInteger")]
    public static partial bool TryReadInt32(nuint address, bool signed, out int value);
}
```

## When to suppress

Do not suppress it in a plugin project: the diagnostic means the binding has no generated body. Reshape the declaration
instead.
