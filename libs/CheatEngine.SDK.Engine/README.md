# CheatEngine.SDK.Engine

The typed Cheat Engine object model for CheatEngine.SDK plugins: object handles, explicit ownership, addresses,
zero-based indices and Cheat Engine's numeric constants.

## Objective

Give plugin code a small, typed vocabulary for the objects and values that Cheat Engine's Lua API hands out. A plugin
reads and writes them without hand-written stack code.

## Why it exists

Cheat Engine exposes its objects as Lua userdata and its values by Lua conventions. An address is a Lua integer or
hexadecimal text. An object counts from zero, but a table returned by a function counts from one. The plugin destroys
the objects it created and never the ones Cheat Engine owns. Each rule is easy to break, and a mistake shows up inside
Cheat Engine. This library encodes each rule once, in a type.

## How it works

| Namespace                             | Types                                           | Role                                                                                                  |
|---------------------------------------|-------------------------------------------------|-------------------------------------------------------------------------------------------------------|
| `CheatEngine.SDK.Engine.Objects`      | `CEObject`, `ICEObject<TSelf>`                  | Borrowed handle: the native object pointer, equal by identity, with property, index and method access |
| `CheatEngine.SDK.Engine.Objects`      | `Owned<T>`                                      | Ownership of an object the plugin created; `Dispose` destroys it                                      |
| `CheatEngine.SDK.Engine.Values`       | `Address`                                       | An address read from a Lua integer or hexadecimal text; its own Lua marshaller                        |
| `CheatEngine.SDK.Engine.Values`       | `IndexBase`, `LuaSequence`                      | Zero-based indices over Cheat Engine objects and Lua sequences                                        |
| `CheatEngine.SDK.Engine.Enums`        | Enums, `CEEnumNames`, `EnumMarshaller<TEnum>`   | Numeric constants, their Cheat Engine names, and Lua integer marshalling                              |
| `CheatEngine.SDK.Engine.Runtime`      | `RuntimeInfo`, `RuntimeCapabilities`            | Explicit runtime observations and evidence metadata; never inferred host facts                        |
| `CheatEngine.SDK.Engine.Memory`       | `TargetMemory`, `HostMemory`, `HostAddress`     | Separate target/CE-host scalar, bounded span, target-width pointer, string and byte-table access       |
| `CheatEngine.SDK.Engine.Inspection`   | `EngineInspection`                              | Copied modules, sections, symbols, address resolution and memory-region snapshots                     |
| `CheatEngine.SDK.Engine.Allocation`   | `TargetMemoryAllocator`, `AllocatedRegion`      | Explicit ownership for target allocation, via a reviewed binding seam                                 |
| `CheatEngine.SDK.Engine.Assembly`     | `AutoAssemblerPatcher`, `AutoAssemblerPatch`    | Low-level, single-disable ownership for Auto Assembler `disableInfo`; Client availability remains live-gated |
| `CheatEngine.SDK.Engine.Scanning`     | `AobScanner`, `StringList`, `MemoryScanSession` | AOB result ownership and conservative MemScan/FoundList state transitions                             |
| `CheatEngine.SDK.Engine.AddressLists` | `AddressList`, `MemoryRecord`                   | Borrowed Cheat-Engine GUI handles and strongly typed record identifiers                               |
| `CheatEngine.SDK.Engine.Errors`       | `EngineException` hierarchy, `EngineResourceHandoffException` | Stable distinction between expected CE, unavailable global, Lua, binding and marshalling failures; post-effect ownership publication reports its one cleanup attempt |
| `CheatEngine.SDK.Engine.Generated`    | `MemoryScalars`                                 | Existing generated scalar wrappers for the earlier memory contract                                    |

The CE 7.7 vertical slices add the following public domains. They use the same protected Lua boundary, but their
evidence and availability are intentionally separate: a catalogued Lua name is not a guarantee that every later CE
host has the same contract.

| Namespace                  | Public surface                                                                | Boundary and result contract                                                                                                                                                                                               |
|----------------------------|-------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Runtime`                  | `RuntimeInfo`, `RuntimeCapabilities`, and `RuntimeCapabilityContract`         | An immutable snapshot of explicitly observed version, architecture, pointer-width and availability facts. Unknown remains unknown; an available global does not fill an unobserved ownership, thread or return field.      |
| `Memory`                   | `TargetMemory`, `HostMemory`, `Address`, `HostAddress`, `PointerSize`, `MemoryAccessFailure` | Keeps attached-target addresses distinct from CE-host addresses. Target-width pointer, scalar, bounded-span, and string calls report expected CE/binding/Lua/result failures through `Try*` results; they do not claim a universal GUI-thread rule.       |
| `Inspection`               | `EngineInspection` and module, section, symbol and region value types         | Returns copied managed snapshots. `NotFound` is used only where the CE 7.7 Lua contract documents `nil`; malformed data and Lua failures remain distinct status values.                                                    |
| `Allocation`               | `TargetMemoryAllocator`, `AllocatedRegion`                                    | Models one target allocation as an explicit, single-use owner. It does not infer a GUI-thread requirement from an unspecific CE global, and reports a failed post-effect owner handoff with its one compensation outcome. |
| `Objects` / `Scanning.Aob` | `StringList`, `StringLists`, `AobScanner`                                     | `StringLists.TryCreate` and a successful `AobScanner.TryScan` out value yield `Owned<StringList>` only after a host object is returned. A list borrowed from CE must never be wrapped or destroyed by plugin code.          |
| `Scanning.Values`          | `MemScan`, `FoundList`, `MemoryScanSessions`, `MemoryScanSession`, scan requests and states | The factory creates and owns the scanner/child pair, retains rollback authority through publication, and the session serializes documented state transitions and releases the child before the parent. It is explicitly main-thread-only; the generic `Owned<T>` wrapper is not. |
| `AddressLists`             | `AddressListAccess`, `AddressList`, `MemoryRecord`, `MemoryRecordId`          | The current GUI list and records are borrowed CE-owned handles. The source catalogue does not by itself prove a runtime-enforceable GUI-thread guard, so this API does not declare one yet.                                |
| `Errors`                   | `EngineException` and stable subclasses                                       | Separates expected operation failure, global absence, Lua failure, binding violation and marshalling violation instead of exposing a raw Lua stack error as the public Engine contract.                                    |

The per-capability provenance, minimum CE version, architecture, thread, ownership and return semantics belong to the
versioned [capability matrix](../../documentations/CheatEngine.SDK/capability-matrix.md). Fixture tests validate
managed behavior and the pinned Lua fixture; opt-in live evidence is recorded separately and is not implied by these
wrappers.

A `CEObject` is the native object pointer and nothing else. `CEObject.TryRead` decodes it from a full userdata whose
first pointer-sized field holds the pointer, and `Push` hands it back through the host. A property is `obj.Name`. A
method is `obj.name(args)`, bound to the instance and called without `self`. An element is `obj[i]` with Cheat Engine's
own index. Every access runs under a protected Lua call. `ICEObject<TSelf>` is the shape `Owned<T>` needs from a handle
type: `Handle` and a static `FromHandle`.

The typed members (`TryGetProperty<TMarshaller, TValue>`, `TrySetProperty<TMarshaller, TValue>`, `TryCallMethod` and
`TryCallMethod<TMarshaller, TResult>`) acquire the calling thread's state, restore the stack and return `bool`. They
return `false` for a nil or wrong-kind value and keep no Lua error message. The stack-level members (`TryGetProperty`,
`TrySetProperty`, `TryGetIndex`, `TrySetIndex`, `TryPushMethod` and `TryCallMethod` with a `LuaState`) return a
`LuaStatus` and leave either their results or one error value.

`CEObject` is a `readonly struct` with no `Dispose` and no public destroy member, so destruction belongs to `Owned<T>`,
never to a flag. Construct one only from a handle that a sourced factory returned as plugin-owned or that another owner
transferred. `Owned<T>` does not infer a universal thread affinity: CE 7.7 does not document one for every
`destroy()` implementation. A narrower typed surface, such as `MemoryScanSession`, must carry and enforce its own
evidence-backed thread rule. There is no finalizer. Dispose every owner while the plugin is still enabled; after
detach, destruction cannot begin and the caller must explicitly choose a recorded shutdown path. `Abandon()` gives up
managed cleanup without granting another caller permission to create an owner.

`Address.TryRead` tries hexadecimal text first (optional `0x`, no sign, no decimal form, 64-bit overflow refused). It
then reads a Lua integer by bit reinterpretation, so an address above `long.MaxValue` round trips. `ToString()` gives
uppercase hexadecimal, eight digits when the value fits 32 bits and sixteen otherwise. Every public index is zero-based,
and the only `+ 1` lives in `IndexBase.ToLuaKey`, which the `LuaSequence` extensions on `LuaState` use.

The enums are `VariableType`, `ScanOption`, `RoundingType`, `FastScanMethod`, `BreakpointMethod`, `BreakpointTrigger`,
`ContinueMethod`, `MemoryProtection` (a `uint` flags enum) and `DuplicateHandling`. `CEEnumNames.ToCEName` returns a
member's Cheat Engine identifier such as `vtDword` as UTF-8, or an empty span for an undefined value or a flag
combination. `TryParseCEName` reverses it case-sensitively and also accepts the `vtUnicodeString` alias. Properties that
Cheat Engine publishes through RTTI, such as `MemoryRecord.VarType`, read and write these names as text.
`EnumMarshaller<TEnum>` does not check that a value is a defined member, because Cheat Engine can return combinations.

The repository-internal `CheatEngine.SDK.SourceGenerators.EngineApi` generates `MemoryScalars` at compile time from
`source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/memory-scalars.cheatengine-sdk-api.txt`. All four
methods take an `Address`. `TryReadInt32` and `TryReadInt64` return `false` when the read fails. `WriteInt32` and
`WriteInt64` return the flag Cheat Engine reports and throw `LuaException` when the Lua call fails.

## CE 7.7 vertical slices

The Engine extensions are anchored to the workspace's versioned CE 7.7 evidence corpus. They are fixture contracts
against CE 7.7.0.10621 x64, not a claim that arbitrary CE builds have identical behavior. No normal test starts Cheat
Engine or attaches another process.

`RuntimeInfo` is an immutable snapshot supplied by an integration layer. It deliberately does not turn a legacy
floating-point `getCEVersion` result into a complete file version and does not infer target architecture, pointer
width, ABI, ownership, return semantics or thread affinity. `RuntimeCapabilities` records each observed capability as
available, unavailable or unknown with the evidence fields that are actually known.

`TargetMemory` accepts only target `Address` values; `HostMemory` accepts only `HostAddress`. Neither type converts
implicitly to the other. Both expose signed and unsigned 8/16/32/64-bit scalars, pointers, `float`/`double`, ordered
`Span<byte>`/`ReadOnlySpan<byte>` buffers, and UTF-8 or UTF-16 string forms. The target-qualified pointer overloads take
the caller's observed `PointerSize`, not `IntPtr.Size`: unknown width and a 64-bit value that cannot fit an x86 target
are structured failures. That observation does not make CE's ambient target selection atomic. Detailed byte overloads
report a verified partial prefix or CE-reported partial write count; legacy read overloads remain all-or-nothing. UTF-8
short-buffer calls leave the caller buffer unchanged and return its required byte capacity. No returned buffer borrows
Lua storage. Their `Try*` methods restore the Lua stack and classify `GlobalUnavailable`, protected `LuaError`, expected
read/write failure, destination capacity, target-width, and malformed-result failures through `MemoryAccessFailure`; a
detached plugin still throws as a lifecycle violation. These are fixture contracts, not a claim that live CE transfer,
target architecture, or affinity has been measured.

`EngineInspection` copies cold snapshots of modules, sections, symbols and memory regions into caller buffers. It does
not publish a partial collection when the destination is too small or a later Lua table entry is malformed. Its
`InspectionStatus` keeps `nil`/not-found, unavailable global, protected Lua failure, insufficient destination, and
invalid result separate. The CE 7.7 catalog does not establish affinity for these globals, so these APIs neither
dispatch nor carry a main-thread assertion.

`TargetSelection` keeps a Cheat Engine selection observation separate from a process incarnation. A qualified
incarnation combines the selected PID read from `getOpenedProcessID` with the local process creation time; missing,
malformed, inaccessible and no-target facts remain explicit observations instead of fabricated identities. It neither
opens nor selects a process. It can only compare observations: no inspected CE primitive makes an observation atomic
with a following ambient-target Lua effect, so an external selection transition in that interval, including an unseen
A→B→A sequence, remains unqualified.

`TargetMemoryAllocator` uses `LuaTargetMemoryAllocationOperations` by default and retains
`ITargetMemoryAllocationOperations` as the direct compatibility seam. The production binding preserves CE's optional
target address and page-protection positions. To create an `AllocatedRegion`, an implementation must additionally opt
into `ITargetBoundMemoryAllocationOperations`, which captures a qualified incarnation and refuses a later observed
mismatch without selecting a replacement target. A legacy direct seam remains usable for its original ambient-target
operations, but is not silently converted into an owner with an unverified cleanup target. `AllocateWithOutcome` and
`ReleaseWithOutcome` are additive structured views: they distinguish success, documented negative results, unavailable
globals, protected Lua failures, malformed results, and target-identity refusal without exposing a Lua state or parsing
an error message. `Dispose` is best-effort, no-throw cleanup. Both release paths consume ownership first, so a
potentially partial deallocation is never retried. CE 7.7 has no documented separate post-allocation protection call in
this surface. If CE accepts an allocation but managed owner publication then throws, the allocator attempts the same
target-qualified deallocation once and throws `EngineResourceHandoffException`; its `CleanupOutcome` distinguishes a
confirmed release, a safe refusal against a replacement target, and an effect requiring manual recovery. It never
retries an uncertain native effect.

`AobScanner.TryScanDetailed` retains global-unavailable, protected-Lua-failure, raw `nil`, malformed-result and
successful-list outcomes; `TryScan` keeps its compatible `bool` projection. A valid empty list is still a successful
caller-owned result, not a match classification. `AobScanner.TryScan` returns `bool` and supplies a caller-owned
`Owned<StringList>` through its `out` parameter because CE documents an AOB result list as caller-freed. `StringList`
itself remains a borrowed handle. `MemScan` and `FoundList` are
borrowed handle values, while
`MemoryScanSessions.TryCreate` is the SDK's source-backed CE 7.7 creation path: it immediately owns the returned parent
and child, holds one Lua operation across both calls, retains raw handles until their owner is published, rolls the
child back before its parent on every later failure, and transfers the pair only to `MemoryScanSession`; an ordinary
consumer cannot create an `Owned<MemScan>` or `Owned<FoundList>` manually. `AutoAssemblerPatcher` retains the returned
disable-info table while it is rooted and performs one target-qualified disable if tracking or patch publication fails;
the same `EngineResourceHandoffException` reports whether that compensation was confirmed, safely refused, or uncertain.
The Client must still keep value scanning capability-gated until its opt-in CE 7.7 x64 live scenario validates creation,
cleanup, disable/re-enable, and target changes. The session guards the `firstScan → waitTillDone → initialize → read →
deinitialize` order and rejects worker-thread cleanup while attached because its owned children use the existing SDK
owner destruction contract. That conservative SDK guard is not evidence that CE's catalog itself declares every scan
operation main-thread-only. The raw `MemScan` and `FoundList` handles likewise carry no `MainThreadOnly` metadata before
a live probe establishes one.

`AddressList` and `MemoryRecord` are always borrowed GUI handles, including a record created by
`AddressList.TryCreateMemoryRecord`, because CE adds it to the address list. Their possible GUI affinity is documented
as an inference only: they deliberately have no `MainThreadOnly` metadata until the opt-in CE 7.7 dispatcher probe
establishes an enforceable host contract.

```csharp
using CheatEngine.SDK.Engine.Generated;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

internal static class EngineDemo
{
    // Both methods need an enabled plugin. ListIsEmpty must also run on the main thread.
    internal static bool ListIsEmpty()
    {
        LuaState L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!StringLists.TryCreate(out Owned<StringList>? list)) return false;
        using (list)
        {
            return list.Value.TryClear()
                   && list.Value.TryGetCount(out int count)
                   && count == 0;
        }
    }

    internal static bool TryBump(Address address)
    {
        return MemoryScalars.TryReadInt32(address, out int value) && MemoryScalars.WriteInt32(address, value + 1);
    }
}
```

The library references [`CheatEngine.SDK.Lua`](../CheatEngine.SDK.Lua/README.md), `CheatEngine.SDK.Lua.Interop` and
`CheatEngine.SDK.Annotations`, and not [`CheatEngine.SDK.Hosting`](../CheatEngine.SDK.Hosting/README.md). It reaches
Cheat Engine through `LuaRuntime`, which `CheatEngine.SDK.Hosting` attaches when the plugin is enabled. The assembly
ships in the `CheatEngine.SDK` package under `lib/net10.0`.

## Promise

The tests in `tests/CheatEngine.SDK.Engine.Tests` drive a simulated Cheat Engine object model on a real Lua 5.3 state.

1. `Owned<T>` destroys its object once, never retries a destroy that raised, and only throws from `Dispose` when the
   protected destroy call cannot begin; a failure returned by CE is consumed after the call starts (`OwnedTests`).
2. A Lua error never becomes an exception: typed members return `false`, stack-level members return a `LuaStatus` with
   one error value (`CEObjectTests`).
3. Members that push the object throw `InvalidOperationException` before the plugin is enabled (`CEObjectValueTests`).
4. A typed operation acquires the state once and pushes the object once, on success and on failure
   (`HostCallCountTests`).
5. Warm property, method and address operations allocate nothing (`ZeroAllocationTests`, `AddressLuaTests`). `Owned<T>`
   creation, `Address.ToString`, `LuaError.FromStack` and a `StringMarshaller` read allocate.
6. A string that is not hexadecimal is never read as an address through Lua's number coercion (`AddressLuaTests`).
7. A negative index passed to `IndexBase.ToLuaKey` or a `LuaSequence` extension throws `ArgumentOutOfRangeException`
   before anything is pushed (`IndexBaseTests`, `LuaSequenceTests`).
8. Enum values equal the numbers in Cheat Engine 7.7's `defines.lua`, and every name parses back (`EnumValueTests`,
   `CEEnumNamesTests`).
9. `CheatEngine.SDK.Engine.dll` and its XML documentation ship in the package, and the EngineApi generator never does
   (`PackageContentsTests`). Warnings are errors, so every public member is documented.
10. Runtime facts remain explicit and unknown fields stay unknown; capability observations are immutable copies
    (`RuntimeContractsTests`).
11. Target and host memory cannot cross address spaces implicitly; scalar, span, text and failure paths keep order and
    restore their Lua stack (`MemoryApiTests`).
12. Inspection publishes complete snapshots only, and distinguishes `nil`, malformed result, unavailable global and Lua
    failure (`EngineInspectionTests`).
13. Allocation ownership is consumed once, AOB lists are owned deterministically, and the MemScan/FoundList state
    machine
    destroys its child before its parent (`AllocatedRegionTests`, `AobScannerTests`, `MemoryScanSessionTests`).
14. Address-list and memory-record wrappers remain borrowed and intentionally do not assert an unproven main-thread
    contract (`AddressListValueTests`, `AddressListLuaTests`).
10. Runtime metadata preserves unknown fields; target and host address spaces cannot be mixed; expected memory failures
    do not become exceptions (`RuntimeContractsTests`, `MemoryApiTests`).
11. Module, section, symbol and region calls distinguish documented `nil` from Lua/binding/malformed-result failures
    and never publish a partial copied destination (`EngineInspectionTests`).
12. Allocation, AOB, StringList, scan-session and address-list tests exercise ownership transfer, zero-based access,
    deterministic child-before-parent cleanup, and forbidden scan state transitions (`AllocatedRegionTests`,
    `AobScannerTests`, `StringListTests`, `MemoryScanSessionTests`, `AddressListLuaTests`). These are fixture contracts,
    not a substitute for a controlled CE 7.7 live run.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Engine.Tests -c Debug --fail-skips on
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
the [test project README](../../tests/CheatEngine.SDK.Engine.Tests/README.md).
