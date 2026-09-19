<div align="center">

# Recipe · Remote memory and code

**Borrow a scratch buffer in the game, call one of its functions with your data, and hand the memory back.**

**Level** `Advanced` · **Time** `30 min` · **Needs** `Guide 03`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                            |
|----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A remote call helper, a checked DLL injector and an Auto Assembler patcher                                                                 |
| **You learn**              | Allocating and freeing target memory, `try` and `finally` around it, positional call shapes, validating text before it reaches a script    |
| **You need**               | The bindings pattern from [03 · Calling Cheat Engine](../../03-calling-cheat-engine/README.md)                                             |
| **Cheat Engine functions** | `allocateMemory`, `deAlloc`, `fullAccess`, `writeString`, `writeSmallInteger`, `executeCode`, `executeCodeEx`, `injectDLL`, `autoAssemble` |

## Objective

Call a function inside the target with a string you provide, without leaking the buffer that carries it. Then load a DLL
into the target and patch bytes at a symbol, each with the checks that keep a mistake from reaching the game.

## Why it matters

Everything here changes another process. A leaked allocation stays there until the game exits, a bad path wastes a long
injection wait, and text pasted into an Auto Assembler script is code. The helpers below are small on purpose: each one
does its checks first, and the release of remote memory sits in a `finally` block so no exit path forgets it.

## How it works

### 1. Bind the functions

```csharp
using CESDK.Annotations.Lua;

namespace RemoteKit;

internal static partial class InjectionCalls
{
    [LuaGlobal("allocateMemory")]
    public static partial bool TryAllocate(int size, out nuint address);

    [LuaGlobal("deAlloc")]
    public static partial void Free(nuint address);

    [LuaGlobal("fullAccess")]
    public static partial void FullAccess(nuint address, int size);

    [LuaGlobal("writeString")]
    public static partial bool WriteString(nuint address, string text);

    [LuaGlobal("writeSmallInteger")]
    public static partial bool WriteInt16(nuint address, int value);

    [LuaGlobal("executeCode")]
    public static partial bool TryExecuteCode(nuint address, nuint parameter, out long result);

    [LuaGlobal("executeCodeEx")]
    public static partial bool TryExecuteCodeEx(
        int callMethod, int timeoutMilliseconds, nuint address, nuint argument, out long result);

    [LuaGlobal("injectDLL")]
    public static partial bool TryInjectDll(string path, out bool injected);

    [LuaGlobal("autoAssemble")]
    public static partial bool TryAutoAssemble(string script, out bool assembled);
}
```

`executeCode` runs a `stdcall` function with one parameter and returns what that function returned.
`executeCodeEx` adds the call method (0 for `stdcall`, 1 for `cdecl`) and a timeout in milliseconds. Both are bound in
the plain positional shape, with one integer argument. The typed form, where an argument is a table such as
`{type=2, value=1.5}` to pass a double or a wide string, cannot be a generated binding, because tables are not part of
the type map. Send that shape through [running Lua](../../08-running-lua/README.md) instead.

### 2. Use them with a guaranteed release

```csharp
using System.Text;
using CESDK.Annotations.Lua;
using CESDK.Hosting.Diagnostics;
using CESDK.Lua.Calls;

namespace RemoteKit;

internal static partial class RemoteCalls
{
    private const int StdCall = 0;
    private const int TimeoutMilliseconds = 5000;

    public static bool TryCallWithString(nuint function, string text, out long result)
    {
        result = 0;
        if (!Ascii.IsValid(text)) return false;

        if (!InjectionCalls.TryAllocate(text.Length + 2, out var buffer)) return false;
        try
        {
            return InjectionCalls.WriteString(buffer, text)
                   && InjectionCalls.WriteInt16(buffer + (nuint)text.Length, 0)
                   && InjectionCalls.TryExecuteCodeEx(StdCall, TimeoutMilliseconds, function, buffer, out result);
        }
        finally
        {
            Release(buffer);
        }
    }

    [LuaFunction("my_plugin_call_with_string")]
    public static long CallWithString(nuint function, string text) =>
        TryCallWithString(function, text, out var result)
            ? result
            : throw new InvalidOperationException("The remote call failed.");

    [LuaFunction("my_plugin_inject")]
    public static bool Inject(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            HostLog.Write(HostLogLevel.Warning, $"Injection skipped, no file at {fullPath}.");
            return false;
        }

        return InjectionCalls.TryInjectDll(fullPath, out var injected) && injected;
    }

    [LuaFunction("my_plugin_patch")]
    public static bool Patch(string symbol, string bytes)
    {
        var parts = bytes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!IsSymbol(symbol) || parts.Length == 0 || !parts.All(IsByte)) return false;

        var script = $"[ENABLE]\n{symbol}:\n  db {string.Join(' ', parts)}\n";
        return InjectionCalls.TryAutoAssemble(script, out var assembled) && assembled;
    }

    private static bool IsSymbol(string text) =>
        text.Length > 0 && text.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '+');

    private static bool IsByte(string text) => text.Length == 2 && text.All(char.IsAsciiHexDigit);

    private static void Release(nuint buffer)
    {
        try
        {
            InjectionCalls.Free(buffer);
        }
        catch (LuaException exception)
        {
            HostLog.Write(HostLogLevel.Warning, "deAlloc failed, the remote buffer may remain.", exception);
        }
    }
}
```

The helper allocates two bytes more than the text and writes an explicit zero terminator after it, so the target reads
a properly ended C string whatever the buffer held before. Text is limited to ASCII, which keeps the byte count equal to
the character count.

### 3. Call them from Lua

```lua
local greet = getAddress("game.exe+5A40")
print(my_plugin_call_with_string(greet, "hello"))       -- the integer the target function returned
print(my_plugin_patch("game.exe+1234", "90 90 90 90 90")) -- true when the script assembled
print(my_plugin_inject("helper.dll"))                    -- true, or false when the file is missing
```

## Good to know

| Topic             | Detail                                                                                                                                                                |
|-------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Failure of a call | `TryExecuteCodeEx` returns `false` when the call raises or returns `nil`, and the helper then still frees the buffer                                                  |
| Timeout           | `executeCodeEx` documents its timeout in milliseconds. A timeout of 0 does not wait and does not free the call memory, so the helper never uses it                    |
| Exceptions        | A `[LuaFunction]` that throws reaches Lua as an error, here `System.InvalidOperationException: The remote call failed.`, and the buffer is already freed by then      |
| Script text       | `autoAssemble` runs whatever it receives. `Patch` accepts only symbol characters and two digit hex bytes, so a caller cannot smuggle a second command into the script |
| Protection        | `fullAccess(address, size)` makes a block writable and executable. Use it on your own allocation, never on the game's code                                            |
| Path              | `Path.GetFullPath` resolves a relative name against the current directory of Cheat Engine, so pass an absolute path when you can                                      |

> [!WARNING]
> Injecting a DLL and calling target code are operations on someone else's process. Do them only against software you
> are allowed to modify, and expect a crash in the target, not in Cheat Engine, when a call is wrong.

## Promise

- A `Try` form never throws for a missing global, a raised error or a wrong result kind.
- The remote buffer is released on every exit path of `TryCallWithString`, including a failed write and a failed call.
- Nothing you pass reaches an Auto Assembler script before `Patch` has checked it.
- The generated thunk catches every exception, and the Lua stack returns to its previous height after every call.

## Before you move on

- [ ] `my_plugin_call_with_string` returns the target function's result and leaves no allocation behind.
- [ ] `my_plugin_inject` returns `false` for a path that does not exist, without calling Cheat Engine.
- [ ] `my_plugin_patch` returns `false` for a symbol or a byte list that contains anything but the allowed characters.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
