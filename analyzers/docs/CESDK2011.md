# CESDK2011: Optional or variadic Lua result shape is invalid

|                    |                              |
|--------------------|------------------------------|
| Category           | `CheatEngine.SDK.Generation` |
| Default severity   | Error                        |
| Enabled by default | Yes                          |
| Code fix           | No                           |
| Reported           | While typing and in build    |

## Cause

The results of a `[LuaGlobal]` method do not follow the order the generated body reads them in, or a variadic result pair
is declared where it cannot be generated.

## Why

A binding with an `out LuaOptional<T>` result or a variadic `Span<T> values, out int count` pair calls Lua with
`LUA_MULTRET` and reads the factual number of values the global returned. The results are then read in a fixed order:

1. required results (`out` values and `Span<byte> destination, out int written` copy-out pairs): fewer values than these
   is `LuaOperationStatusKind.MissingResult`, never `NilResult`;
2. `out LuaOptional<T>` results: a position Lua did not return is `Omitted`, a `nil` is `Nil`;
3. at most one variadic `Span<T> values, out int count` pair, last: every remaining value is copied, or the call reports
   `ResultCapacityExceeded` with the needed count.

Only the form that returns `LuaOperationStatus` can report a capacity or element failure of the variadic tail, so the
variadic pair is refused on the `bool` Try form. The generator emits nothing for a declaration that breaks these rules.

## What is checked

| Problem                        | Requirement                                                                                           |
|--------------------------------|-------------------------------------------------------------------------------------------------------|
| `OptionalResultNotTrailing`    | No required result follows an `out LuaOptional<T>` result.                                           |
| `VariadicResultNotLast`        | No result follows the variadic pair.                                                                  |
| `VariadicResultOutsideOutcome` | A variadic pair is declared only by a method that returns `LuaOperationStatus`.                       |
| `UnsupportedVariadicElement`   | The span element is `int`, `long`, `float`, `double`, `bool` or `nuint`. `Span<byte>` stays copy-out. |
| `MultipleVariadicResults`      | At most one variadic pair is declared.                                                                |

## Example

```csharp
using System;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;

namespace MyPlugin;

public static partial class Memory
{
    [LuaGlobal("readBytes")]
    public static partial bool TryReadBytes(nuint address, int count, Span<long> values, out int read);   // CESDK2011
}
```

Compliant: the Outcome form, which reports `ResultCapacityExceeded`, `NilResult` or `InvalidResult` for the tail.

```csharp
public static partial class Memory
{
    [LuaGlobal("readBytes")]
    public static partial LuaOperationStatus ReadBytes(nuint address, int count, Span<long> values, out int read);
}
```

## When to suppress

Do not suppress it: the diagnostic means the binding has no generated body. Reorder or reshape the results.
