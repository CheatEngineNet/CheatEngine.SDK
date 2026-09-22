<div align="center">

# Recipe · Assembly

**Apply one reversible Auto Assembler patch, then inspect or assemble individual instructions through an explicit target
profile.**

**Level** `Advanced` · **Time** `30 min` · **Needs** `Guide 03`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                                 |
|----------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A reversible Auto Assembler patch, a bounded disassembly listing, and a bounded one-line assembler                                              |
| **You learn**              | The difference between a script lifecycle and profile-qualified instruction operations                                                          |
| **You need**               | [03 · Calling Cheat Engine](../../03-calling-cheat-engine/README.md) and the toolkit tour of [08 · Running Lua](../../08-running-lua/README.md) |
| **Cheat Engine functions** | `autoAssemble`, `assemble`, `disassemble`, `splitDisassembledString`, `getInstructionSize`, and `getPreviousOpcode`                             |

## Objective

Patch code reversibly, read a short instruction listing, and assemble a line without exposing Cheat Engine's
historical classic instruction slots or manually borrowing Lua display text.

## Why it matters

`autoAssemble` is a script lifecycle: its successful result contains the one disable-info table needed to execute
`[DISABLE]`. It is not the same operation as assembling one textual instruction. For individual instructions, the SDK
first observes an `InstructionTargetProfile` from the selected PID and CE target probes. It then validates each target
address against that observed profile, bounds every result, and reports a factual failure status.

The observation is deliberately not a target lock. Cheat Engine can change its ambient target after a PID check; each
individual operation therefore checks the PID again before and after its Lua call. A live Cheat Engine assembler,
target ISA, relocation backend, thread affinity, and Native AOT plugin loading are not qualified by this fixture-backed
recipe.

## How it works

### 1. Observe an instruction profile

```csharp
using CheatEngine.SDK.Engine.Assembly;

InstructionOperationStatus profileStatus = InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile target);
if (profileStatus != InstructionOperationStatus.Success)
{
    // No selected target, unavailable CE probe, inconsistent ISA facts, or a protected Lua failure.
    return;
}

// target.Profile is an x86/x64/ARM32/ARM64 fact with its matching address width.
// It validates addresses; it never changes Cheat Engine's selected target or assembler configuration.
```

Keep this value only for a short operation. A later selection transition, including an unseen A→B→A transition, is not
made safe by the observation.

### 2. Apply a reversible Auto Assembler patch

```csharp
using CheatEngine.SDK.Engine.Assembly;

const string script = """
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

using AutoAssemblerPatch patch = AutoAssemblerPatcher.Apply(script);
// The patch owns CE's disable-info table. Dispose attempts [DISABLE] once and never retries an uncertain result.
```

`AutoAssemblerPatcher` owns the returned disable-info table and validates its target incarnation before cleanup. It
never supplies `targetSelf`, opens a process, or silently reselects a target. If `Dispose` records
`RequiresManualRecovery`, retain that fact for operator recovery instead of replaying `[DISABLE]`.

### 3. Read code and assemble a line

```csharp
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Values;

namespace AssemblyRecipe;

internal static class Listing
{
    internal static InstructionOperationStatus TryRead(
        InstructionTargetProfile target,
        Address start,
        int count,
        List<InstructionDisassembly> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(destination);

        var address = start;
        for (var index = 0; index < count; index++)
        {
            var status = InstructionDisassembler.TryDisassemble(target, address, maximumUtf8Bytes: 512,
                out var instruction, out _);
            if (status != InstructionOperationStatus.Success) return status;

            status = InstructionNavigator.TryGetLength(target, address, out var length);
            if (status != InstructionOperationStatus.Success) return status;

            destination.Add(instruction);
            address += length;
        }

        return InstructionOperationStatus.Success;
    }

    internal static InstructionOperationStatus TryAssemble(
        InstructionTargetProfile target,
        string line,
        Address origin,
        Span<byte> destination,
        out int written,
        out int requiredLength)
    {
        // CE receives origin every time: it is the explicit base for a relative operand such as "jmp rel".
        // A short destination returns DestinationTooSmall with requiredLength and does not write a byte prefix.
        return InstructionAssembler.TryAssemble(target, line, origin, destination, out written, out requiredLength);
    }
}
```

`InstructionDisassembler` copies and bounds the raw UTF-8 display line before it asks CE to split the line, then
checks the collective byte length of the four raw fields before it decodes any of them. The returned
`InstructionDisassembly` fields are managed strings, so the caller does not parse or retain Lua UI text.
`InstructionAssembler` validates every element in CE's byte table before it copies anything into the caller's span.
The `TargetChanged`, `InvalidProfile`, `AddressExceedsProfileWidth`, `DestinationTooSmall`, `OutputTooLong`,
`InstructionRejected`, `GlobalUnavailable`, `LuaFailure`, and `InvalidResult` outcomes are intentionally distinct.

`InstructionNavigator.TryGetPrevious` is available for display tooling, but it keeps Cheat Engine's own estimated
previous-opcode semantics. It is not a safe general-purpose backwards decoder. The historical native `Assembler`,
`Disassembler`, `disassembleEx`, `previousOpcode`, and `nextOpcode` slots are not this API: their ABI and string-buffer
evidence has unresolved conflicts, so the SDK does not project them.

## Good to know

- Keep an `AutoAssemblerPatch` alive for the whole enabled period and dispose it while the Lua runtime is still
  attached.
- Treat a profile as an observed validation input, not a request to configure CE. Re-observe after a meaningful target
  transition.
- Select a raw UTF-8 disassembly bound suitable for your UI. A longer CE line, or collectively longer split fields,
  is rejected before the SDK decodes it.
- Retry assembly with a larger caller-owned buffer only after handling `DestinationTooSmall`; the initial call publishes
  no partial bytes.
- `getPreviousOpcode` is an estimate. Use it for display navigation, not patch planning.

## Promise

- Auto Assembler cleanup consumes the disable-info owner before its one disable attempt and does not replay uncertain
  work.
- A failed instruction operation leaves no borrowed Lua string, Lua table, native disassembler object, or partial
  assembly prefix in managed output.
- Every instruction address is checked against the target profile's explicit width, never `IntPtr.Size`.
- Fixture tests exercise the managed Lua shapes and negative paths; they are not evidence of a live Cheat Engine
  qualification.

## Before you move on

- [ ] Verify the patch's `[DISABLE]` branch against an authorized disposable target before shipping an application
  workflow.
- [ ] Choose a maximum UTF-8 display-line length and a caller-owned assembly buffer for your UI.
- [ ] Record a controlled live capture before claiming support for a particular CE host and target ISA.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
