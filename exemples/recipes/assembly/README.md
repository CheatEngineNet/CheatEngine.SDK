<div align="center">

# Recipe · Assembly

**Toggle an Auto Assembler patch from C#, list the instructions at an address, and assemble a line on demand.**

**Level** `Advanced` · **Time** `30 min` · **Needs** `Guide 03`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                                 |
|----------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A patch you can switch on and off, a disassembly listing, an instruction size query and a line assembler                                        |
| **You learn**              | Keeping an Auto Assembler `[DISABLE]` state alive with a registry reference, and reading Lua strings and tables into C#                         |
| **You need**               | [03 · Calling Cheat Engine](../../03-calling-cheat-engine/README.md) and the toolkit tour of [08 · Running Lua](../../08-running-lua/README.md) |
| **Cheat Engine functions** | `autoAssemble`, `assemble`, `disassemble`, `splitDisassembledString`, `getInstructionSize`, `getPreviousOpcode`, `getComment`, `setComment`     |

## Objective

Patch code in the target and undo the patch cleanly, and read the code around an address, all from C#.

## Why it matters

An Auto Assembler script has two halves. `[ENABLE]` installs the patch, and `[DISABLE]` removes it. Cheat Engine hands
you a table of disable information when the enable succeeds, and it needs that same table back to run `[DISABLE]`. A
plugin that forgets the table can install a patch it can never take out. The recipe keeps the table in a registry
reference, so the patch stays reversible.

## How it works

### 1. Bind the simple functions

```csharp
using System.Diagnostics.CodeAnalysis;
using CESDK.Annotations.Lua;

namespace AssemblyRecipe;

internal static partial class AsmCalls
{
    [LuaGlobal("disassemble")]
    public static partial bool TryDisassemble(nuint address, [MaybeNullWhen(false)] out string line);

    [LuaGlobal("splitDisassembledString")]
    public static partial bool TrySplit(
        string line,
        [MaybeNullWhen(false)] out string address,
        [MaybeNullWhen(false)] out string bytes,
        [MaybeNullWhen(false)] out string opcode,
        [MaybeNullWhen(false)] out string extra);

    [LuaGlobal("getInstructionSize")]
    public static partial bool TryGetInstructionSize(nuint address, out int size);

    [LuaGlobal("getPreviousOpcode")]
    public static partial bool TryGetPreviousOpcode(nuint address, out nuint previous);

    [LuaGlobal("getComment")]
    public static partial bool TryGetComment(nuint address, [MaybeNullWhen(false)] out string comment);

    [LuaGlobal("setComment")]
    public static partial void SetComment(nuint address, string text);
}
```

`splitDisassembledString` returns four strings, so the Try form ends with four `out` results. A binding reads exactly as
many results as you declare.

### 2. A patch you can switch on and off

```csharp
using CESDK.Annotations.Lua;
using CESDK.Lua.Marshalling;
using CESDK.Lua.References;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace AssemblyRecipe;

internal sealed class AutoAssemblerToggle(string script) : IDisposable
{
    private LuaRef? _disableInfo;

    public bool IsEnabled => _disableInfo is { IsCurrent: true };

    public bool Enable()
    {
        if (IsEnabled) return true;

        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!L.TryGetGlobal("autoAssemble"u8).IsOk) return false;

        StringMarshaller.Push(L, script);
        if (!L.TryCall(1, 2).IsOk || !L.ToBoolean(-2)) return false;

        _disableInfo = L.CreateRef();
        return true;
    }

    public bool Disable()
    {
        var info = Interlocked.Exchange(ref _disableInfo, null);
        if (info is null) return false;

        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        var ok = L.TryGetGlobal("autoAssemble"u8).IsOk;
        if (ok)
        {
            StringMarshaller.Push(L, script);
            ok = L.TryPushRef(info) && L.TryCall(2, 1).IsOk && L.ToBoolean(-1);
        }

        info.Release(L);
        return ok;
    }

    public void Dispose() => Disable();
}

internal static partial class Patches
{
    private const string NoRecoilScript = """
        [ENABLE]
        aobscanmodule(recoilWrite, game.exe, F3 0F 11 83 A0 01 00 00)
        registersymbol(recoilWrite)
        recoilWrite:
          nop 8

        [DISABLE]
        recoilWrite:
          db F3 0F 11 83 A0 01 00 00
        unregistersymbol(recoilWrite)
        """;

    private static readonly AutoAssemblerToggle s_noRecoil = new(NoRecoilScript);

    [LuaFunction("my_plugin_no_recoil")]
    public static bool NoRecoil(bool enable) => enable ? s_noRecoil.Enable() : s_noRecoil.Disable();

    // Call from OnDisable, while the Lua runtime is still attached, so the game is left as it was found.
    public static void RestoreAll() => s_noRecoil.Dispose();
}
```

`autoAssemble(script)` returns `true` and the disable table when `[ENABLE]` succeeds. `CreateRef` pops that table into
the Lua registry, and `Disable` pushes it back as the second argument, which is what makes Cheat Engine run `[DISABLE]`.
The reference is released on every path, and `TryPushRef` refuses a reference from an earlier enable instead of pushing
a stale value.

The script is a raw string literal, so it keeps its own indentation and needs no escaping. The bytes are an example: use
the pattern and the original bytes of your own target.

### 3. Read code and assemble a line

```csharp
using CESDK.Annotations.Lua;
using CESDK.Engine.Values;
using CESDK.Lua.Marshalling;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace AssemblyRecipe;

internal readonly record struct Instruction(Address Address, string Bytes, string Opcode, string Extra);

internal static partial class Listing
{
    public static List<Instruction> Read(Address start, int count)
    {
        List<Instruction> lines = new(count);
        var address = start;
        for (var i = 0; i < count; i++)
        {
            var target = unchecked((nuint)address.ToUInt64());
            if (!AsmCalls.TryDisassemble(target, out var text)) break;
            if (!AsmCalls.TrySplit(text, out _, out var bytes, out var opcode, out var extra)) break;
            if (!AsmCalls.TryGetInstructionSize(target, out var size) || size <= 0) break;

            lines.Add(new Instruction(address, bytes, opcode, extra));
            address += size;
        }

        return lines;
    }

    public static bool TryAssemble(string line, Address at, out byte[] bytes)
    {
        bytes = [];
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!L.TryGetGlobal("assemble"u8).IsOk) return false;

        StringMarshaller.Push(L, line);
        Address.Push(L, at);
        if (!L.TryCall(2, 1).IsOk || !L.IsTable(-1)) return false;

        var table = L.AbsoluteIndex(-1);
        var count = L.RawSequenceCount(table);
        var result = new byte[count];
        for (var i = 0; i < count; i++)
        {
            L.RawGetSequenceItem(table, i);
            if (!L.TryReadInteger(-1, out var value) || value is < 0 or > 255) return false;

            result[i] = (byte)value;
            L.Pop(1);
        }

        bytes = result;
        return true;
    }

    [LuaFunction("my_plugin_disassemble")]
    public static string Disassemble(long address, long count)
    {
        var lines = Read(Address.FromInt64(address), (int)Math.Clamp(count, 1, 64));
        return string.Join('\n', lines.Select(l => $"{l.Address}  {l.Bytes,-16}{l.Opcode} {l.Extra}".TrimEnd()));
    }

    [LuaFunction("my_plugin_instruction_size")]
    public static long InstructionSize(long address) =>
        AsmCalls.TryGetInstructionSize(unchecked((nuint)address), out var size) ? size : -1;

    [LuaFunction("my_plugin_assemble")]
    public static string? AssembleHex(string line, long address) =>
        TryAssemble(line, Address.FromInt64(address), out var bytes) ? Convert.ToHexString(bytes) : null;

    [LuaFunction("my_plugin_annotate")]
    public static bool Annotate(long address, string text)
    {
        var target = unchecked((nuint)address);
        if (AsmCalls.TryGetComment(target, out var existing) && existing.Length > 0) return false;

        AsmCalls.SetComment(target, text);
        return true;
    }
}
```

`Disassemble` walks instruction by instruction: it disassembles an address, splits the line into its four fields, asks
for the instruction size, and steps forward by that size. `assemble` answers with a Lua table of byte values, so
`TryAssemble` reads it as a zero based sequence and checks that every value fits in a byte.

### 4. Try it

```lua
print(my_plugin_no_recoil(true))       -- true: the patch is installed
print(my_plugin_no_recoil(false))      -- true: [DISABLE] ran with the saved information
print(my_plugin_disassemble(getAddress("game.exe"), 4))
print(my_plugin_instruction_size(getAddress("game.exe")))
print(my_plugin_assemble("jmp 0x140001000", getAddress("game.exe")))
```

```text
true
true
0000000140000000  48 89 5C 24 08  mov [rsp+08],rbx
0000000140000005  57              push rdi
0000000140000006  48 83 EC 20     sub rsp,20
000000014000000A  48 8B D9        mov rbx,rcx
5
E9FB0F0000
```

The listing and the bytes depend on your target. `my_plugin_assemble` returns `nil` when Cheat Engine cannot assemble
the line.

## Good to know

- **Keep the toggle for the whole enable.** Call `Patches.RestoreAll()` from `OnDisable`. That method runs while the Lua
  runtime is still attached, so `[DISABLE]` can still reach Cheat Engine.
- **The script text decides the target.** `autoAssemble(text, targetSelf)` can assemble into Cheat Engine itself. The
  toggle above patches the opened process, which is what a trainer wants.
- **`getPreviousOpcode` is a guess.** Cheat Engine documents the answer as an estimate, so use it to step backwards for
  display and not to rewrite code.
- **Comments are per address.** `Annotate` refuses to overwrite a comment that already exists, so a plugin never erases
  a note that a person wrote.
- **A binding reads only the results you declare.** `autoAssemble` returns two values, and the toolkit code above asks
  for both because it needs the table.

## Promise

- A failed `[ENABLE]` returns `false` and leaves no reference behind.
- `Disable` releases its registry reference on every path and never runs `[DISABLE]` twice.
- A Try form never throws for an address that does not disassemble. It returns `false`.
- The Lua stack returns to its previous height after every call.

## Before you move on

- [ ] `my_plugin_no_recoil(true)` followed by `my_plugin_no_recoil(false)` returns `true` twice and restores the bytes.
- [ ] `my_plugin_disassemble(address, 4)` prints four instructions whose addresses advance by each instruction size.
- [ ] `Patches.RestoreAll()` is called from your `OnDisable`.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
