<div align="center">

# 02 · Lua functions

**Turn any static C# method into a Lua global, with no glue code.**

**Level** `Beginner` · **Time** `15 min` · **Needs** `Guide 01`

[Examples index](../README.md) · [Previous: First plugin](../01-first-plugin/README.md) · [Next: Calling Cheat Engine](../03-calling-cheat-engine/README.md)

</div>

---

|                |                                                                                       |
|----------------|---------------------------------------------------------------------------------------|
| **You build**  | A "game math" plugin whose helpers are callable from any cheat table script           |
| **You learn**  | `[LuaFunction]`, the type map, registration, and what Lua sees when a call goes wrong |
| **You need**   | The project from [01 · Your first plugin](../01-first-plugin/README.md)               |
| **Attributes** | `[CheatEnginePlugin]`, `[LuaFunction]`                                                |

## Objective

Publish C# logic to Lua so that cheat tables, the Lua Engine window and other plugins can call it. You write an
ordinary method and one attribute.

## Why it matters

A function that Lua calls has to be an unmanaged `cdecl` function. It must check every argument, catch every exception
and report failure through Lua's own error channel. Written by hand, each function repeats that code and every exit
path can go wrong. The generator writes it once, correctly, for every method you mark.

## How it works

### 1. Declare the functions

```csharp
using System.Text;
using CESDK.Annotations.Lua;
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace GameMath;

[CheatEnginePlugin("Game Math")]
public sealed class GameMathPlugin : CheatEnginePlugin
{
    protected override void OnEnable()
    {
        var status = Functions.RegisterLuaFunctions(LuaRuntime.AcquireState());
        if (!status.IsOk) throw new InvalidOperationException($"Lua registration failed: {status}.");
    }

    protected override void OnDisable() => Functions.UnregisterLuaFunctions(LuaRuntime.AcquireState());
}

internal static partial class Functions
{
    private static readonly string[] s_items = ["potion", "elixir", "phoenix down"];

    [LuaFunction("my_plugin_add")]
    public static long Add(long left, long right) => left + right;

    [LuaFunction("my_plugin_hp_percent")]
    public static double HpPercent(long current, long max) => max <= 0 ? 0 : 100.0 * current / max;

    [LuaFunction("my_plugin_is_alive")]
    public static bool IsAlive(long health) => health > 0;

    [LuaFunction("my_plugin_hex")]
    public static string Hex(long value) => $"0x{value:X}";

    [LuaFunction("my_plugin_find_item")]
    public static string? FindItem(long index) => index >= 0 && index < s_items.Length ? s_items[index] : null;

    [LuaFunction("my_plugin_checksum")]
    public static long Checksum(ReadOnlySpan<byte> text)
    {
        long sum = 0;
        foreach (var b in text) sum = sum * 31 + b;
        return sum;
    }

    [LuaFunction("my_plugin_divide")]
    public static long Divide(long dividend, long divisor) => dividend / divisor;

    [LuaFunction("my_plugin_kind")]
    public static string Kind(LuaState state, ReadOnlySpan<byte> globalName)
    {
        using LuaFrame frame = new(state);
        if (!state.TryGetGlobal(globalName).IsOk) return "error";
        return Encoding.UTF8.GetString(state.TypeName(state.TypeOf(-1)));
    }
}
```

Each method is a plain static method. The attribute carries the Lua name, and the generator turns the parameters and
the return value into Lua arguments and results.

### 2. Register on enable, unregister on disable

`RegisterLuaFunctions(state)` and `UnregisterLuaFunctions(state)` are generated on your `partial` type. Both return a
`LuaStatus`. Registration assigns each function to its global, and unregistration assigns `nil` to each name.

The plugin above throws when registration fails. `OnEnable` may throw: the host logs the exception and tells Cheat
Engine that the enable failed. Always unregister in `OnDisable`, so that a disabled plugin leaves no globals behind.

### 3. Call them from Lua

```lua
print(my_plugin_add(2, 3))              -- 5
print(my_plugin_hp_percent(50, 200))    -- 25.0
print(my_plugin_is_alive(0))            -- false
print(my_plugin_hex(6699))              -- 0x1A2B
print(my_plugin_find_item(1))           -- elixir
print(my_plugin_find_item(99))          -- nil
print(my_plugin_checksum("abc"))        -- 96354
print(my_plugin_kind("print"))          -- function
```

| Lua call                           | Result               | Why                                                                      |
|------------------------------------|----------------------|--------------------------------------------------------------------------|
| `my_plugin_add(2, 3)`              | `5`                  | Two Lua integers in, one integer out                                     |
| `my_plugin_add(2.0, 3)`            | `5`                  | A float with an integral value converts to an integer                    |
| `my_plugin_hp_percent(50, 200)`    | `25.0`               | A `double` result is a Lua number                                        |
| `my_plugin_hex(-1)`                | `0xFFFFFFFFFFFFFFFF` | `long` keeps its bits, and `X` prints them as unsigned                   |
| `my_plugin_find_item(99)`          | `nil`                | A `null` string result becomes `nil`                                     |
| `my_plugin_kind("no_such_global")` | `nil`                | The leading `LuaState` is the callback's state and is not a Lua argument |

## The type map

| Lua type | C# types               | Allowed as                                        |
|----------|------------------------|---------------------------------------------------|
| integer  | `int`, `long`, `nuint` | Parameters and results                            |
| number   | `float`, `double`      | Parameters and results                            |
| boolean  | `bool`                 | Parameters and results                            |
| string   | `string`, `string?`    | Parameters and results                            |
| string   | `ReadOnlySpan<byte>`   | Parameters and results of `[LuaFunction]` methods |

> [!TIP]
> Take text as `ReadOnlySpan<byte>` when the function runs often. The span points at the string Lua already holds, so
> the call copies nothing and allocates nothing. `my_plugin_checksum` reads its argument this way.

A leading `LuaState` parameter, as in `my_plugin_kind`, receives the state Lua called you with. It gives you the whole
[running Lua](../08-running-lua/README.md) toolkit inside the function and does not count as a Lua argument. Guard the
stack with `using LuaFrame frame = new(state);` whenever you push values.

## When a call goes wrong

Lua reports the mistake and keeps running. No exception ever reaches Cheat Engine, and the Lua stack returns to the
height it had before the call.

| Lua call                   | What Lua reports                                             |
|----------------------------|--------------------------------------------------------------|
| `my_plugin_add(1)`         | `wrong number of arguments to 'my_plugin_add' (2 expected)`  |
| `my_plugin_add("a", 2)`    | `bad argument #1 (integer expected, got string)`             |
| `my_plugin_add(1.5, 2)`    | `bad argument #1 (integer expected, got number)`             |
| `my_plugin_is_alive(true)` | `bad argument #1 (integer expected, got boolean)`            |
| `my_plugin_divide(10, 0)`  | `System.DivideByZeroException: Attempted to divide by zero.` |

A C# exception becomes an ordinary Lua error, so a script can guard the call with `pcall`:

```lua
local ok, message = pcall(my_plugin_divide, 10, 0)
print(ok, message)   -- false   System.DivideByZeroException: Attempted to divide by zero.
```

## Naming and shape rules

| Rule       | Detail                                                                                                        |
|------------|---------------------------------------------------------------------------------------------------------------|
| Lua name   | Starts with an ASCII letter or `_`, continues with letters, digits or `_`, and is not a Lua 5.3 reserved word |
| Method     | `static`, non generic, not `async`, parameters by value, no `params`, no default values                       |
| Type       | The containing type and every type around it is `partial`, non generic and not `file` local                   |
| Duplicates | Two methods of one type with the same Lua name are both skipped, and `CESDK2003` names the clash              |
| Prefix     | Give every name a plugin prefix such as `my_plugin_`. Lua globals are shared with every table and plugin      |

> [!NOTE]
> A method the generator cannot bind is never silently ignored. The analyzers report it in the editor with a
> `CESDK2001` to `CESDK2004` diagnostic and a page that explains the fix.
See [11 · Diagnostics](../11-diagnostics/README.md).

## Promise

- A call with the wrong number or kind of arguments raises a Lua error that names the function or the argument.
- An exception thrown by your method becomes a Lua error, and none reaches native code.
- The Lua stack returns to its previous height after every call.
- The generated thunk allocates nothing on a warm call. What your own method allocates is yours.

## Before you move on

- [ ] `print(my_plugin_add(2, 3))` prints `5`.
- [ ] `print(pcall(my_plugin_divide, 10, 0))` prints `false` and the exception text.
- [ ] After unticking the plugin, `my_plugin_add` is `nil`.

---

<div align="center">

[Examples index](../README.md) · **Next:** [03 · Calling Cheat Engine](../03-calling-cheat-engine/README.md)

</div>
