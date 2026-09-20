<div align="center">

# Recipe · Slow motion and file fingerprints

**Slow the game down and bring it back exactly as it was, then check that the binary is the one you tested against.**

**Level** `Beginner` · **Time** `15 min` · **Needs** `Guide 03`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                           |
|----------------------------|---------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A slow motion toggle that remembers the previous speed, a binary verifier and two text converters                         |
| **You learn**              | Speed bindings, hashing a file, a string or a memory range, restoring state in `OnDisable`, nullable `out string` results |
| **You need**               | The bindings pattern from [03 · Calling Cheat Engine](../../03-calling-cheat-engine/README.md)                            |
| **Cheat Engine functions** | `speedhack_setSpeed`, `speedhack_getSpeed`, `stringToMD5String`, `md5file`, `md5memory`, `ansiToUTF8`, `UTF8ToAnsi`       |

## Objective

Give a cheat table two switches it can call from any script. One slows the target to a quarter of its speed and puts the
previous speed back, whatever that was. The other checks the game executable against a known MD5 hash before the plugin
touches an address.

## Why it matters

A speed toggle that assumes the normal speed is 1.0 breaks the moment the player already set something else.
Remembering the previous value, and restoring it when the plugin is disabled, makes the toggle safe to leave on a table.
A hash check saves you from writing to a game version whose addresses have moved.

## How it works

### 1. Bind the functions

```csharp
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lua;

namespace TimeTools;

internal static partial class SpeedCalls
{
    [LuaGlobal("speedhack_setSpeed")]
    public static partial void SetSpeed(double speed);

    [LuaGlobal("speedhack_getSpeed")]
    public static partial bool TryGetSpeed(out double speed);

    [LuaGlobal("stringToMD5String")]
    public static partial bool TryMd5(string text, [MaybeNullWhen(false)] out string hash);

    [LuaGlobal("md5file")]
    public static partial bool TryMd5File(string path, [MaybeNullWhen(false)] out string hash);

    [LuaGlobal("md5memory")]
    public static partial bool TryMd5Memory(nuint address, int size, [MaybeNullWhen(false)] out string hash);

    [LuaGlobal("ansiToUTF8")]
    public static partial bool TryAnsiToUtf8(ReadOnlySpan<byte> ansi, Span<byte> destination, out int written);

    [LuaGlobal("UTF8ToAnsi")]
    public static partial bool TryUtf8ToAnsi(ReadOnlySpan<byte> utf8, Span<byte> destination, out int written);
}
```

`speedhack_setSpeed` turns the speed control on if needed and sets the factor. `speedhack_getSpeed` returns the last
speed that was set, so it is a Try form: a script that never set one leaves nothing to read.

### 2. Write the tools

```csharp
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Runtime;

namespace TimeTools;

[CheatEnginePlugin("Time Tools")]
public sealed class TimeToolsPlugin : CheatEnginePlugin
{
    protected override void OnEnable()
    {
        var status = Tools.RegisterLuaFunctions(LuaRuntime.AcquireState());
        if (!status.IsOk) throw new InvalidOperationException($"Lua registration failed: {status}.");
    }

    protected override void OnDisable()
    {
        Tools.RestoreSpeed();
        Tools.UnregisterLuaFunctions(LuaRuntime.AcquireState());
    }
}

internal static partial class Tools
{
    private const double NormalSpeed = 1.0;
    private const double SlowSpeed = 0.25;
    private static double s_previousSpeed = NormalSpeed;
    private static bool s_slow;

    [LuaFunction("my_plugin_slowmo")]
    public static double SlowMotion(bool on)
    {
        if (!on) RestoreSpeed();
        else if (!s_slow)
        {
            var previous = SpeedCalls.TryGetSpeed(out var current) ? current : NormalSpeed;
            SpeedCalls.SetSpeed(SlowSpeed);
            s_previousSpeed = previous;
            s_slow = true;
        }

        return SpeedCalls.TryGetSpeed(out var speed) ? speed : NormalSpeed;
    }

    public static bool RestoreSpeed()
    {
        if (!s_slow) return true;

        SpeedCalls.SetSpeed(s_previousSpeed);
        s_slow = false;
        return true;
    }

    [LuaFunction("my_plugin_verify_binary")]
    public static bool VerifyBinary(string path, string expectedMd5) =>
        SpeedCalls.TryMd5File(path, out var actual) && string.Equals(actual, expectedMd5, StringComparison.OrdinalIgnoreCase);

    [LuaFunction("my_plugin_verify_memory")]
    public static bool VerifyMemory(nuint address, int size, string expectedMd5) =>
        SpeedCalls.TryMd5Memory(address, size, out var actual) && string.Equals(actual, expectedMd5, StringComparison.OrdinalIgnoreCase);

    [LuaFunction("my_plugin_fingerprint")]
    public static string? Fingerprint(string text) => SpeedCalls.TryMd5(text, out var hash) ? hash : null;

    public static bool TryAnsiToUtf8(ReadOnlySpan<byte> ansi, Span<byte> destination, out int written) =>
        SpeedCalls.TryAnsiToUtf8(ansi, destination, out written);

    public static bool TryUtf8ToAnsi(ReadOnlySpan<byte> utf8, Span<byte> destination, out int written) =>
        SpeedCalls.TryUtf8ToAnsi(utf8, destination, out written);
}
```

`SlowMotion` reads the current speed before it changes anything, and it stores the remembered value only after
`SetSpeed` succeeded, so a failure leaves the tool in its old state. Asking for slow motion twice keeps the first
remembered speed. `RestoreSpeed` does not clear its recovery state until `SetSpeed` returns, so a pre-dispatch failure
can be retried. `OnDisable` calls it while the Lua runtime is still attached.

### 3. Call them from Lua

```lua
print(my_plugin_slowmo(true))        -- 0.25
print(my_plugin_slowmo(false))       -- the speed from before, 1.0 unless you had set another
print(my_plugin_verify_binary("game.exe", "0123456789abcdef0123456789abcdef"))
print(my_plugin_fingerprint("hello"))
```

## Good to know

| Topic            | Detail                                                                                                                                                  |
|------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------|
| Nullable results | A `string?` result that is `null` reaches Lua as `nil`, so a failed hash is `nil` and never an empty string                                             |
| Comparison       | MD5 output is hexadecimal text, so the comparison ignores case                                                                                          |
| Path             | `md5file` takes a path the way Cheat Engine resolves it. Pass the full path of the executable when the working folder is unclear                        |
| Memory           | `my_plugin_verify_memory` hashes a range of the target, which detects a patched module that the file on disk does not show                              |
| Text             | `ansiToUTF8` and `UTF8ToAnsi` exchange Windows-codepage bytes, not managed UTF-8 strings. Keep them as raw byte spans and decode only with the code page your integration selected |
| Byte tables      | Cheat Engine's byte table converters, such as `dwordToByteTable`, need no binding. In C# use `BitConverter` or `BinaryPrimitives` on a `Span<byte>`     |
| Whole system     | `dbvm_speedhack_setSpeed` also exists and slows the whole system clock. It is covered in the [DBVM recipe](../dbvm/README.md)                           |

## Promise

- A Try form returns `false` and leaves `out` results at their defaults when a function is missing, raises or returns
  the wrong kind, so a failed hash is never mistaken for a match.
- `RestoreSpeed` runs in `OnDisable` before the functions are unregistered and retains its saved speed if the setter throws.
- The generated thunk catches every exception, and the Lua stack returns to its previous height after every call.
- Hash results come back as `string`, so each hash call allocates one string; byte-span text conversion leaves decoding
  and allocation to the caller's chosen Windows code page.

## Before you move on

- [ ] `my_plugin_slowmo(true)` then `my_plugin_slowmo(false)` leaves the speed exactly where you found it.
- [ ] Unticking the plugin while slow motion is on restores the previous speed.
- [ ] `my_plugin_verify_binary` returns `false` for a wrong hash and for a file that does not exist.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
