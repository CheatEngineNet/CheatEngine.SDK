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

| Namespace                            | Types                                                                                                                                          | Role                                                                                                                                                                 |
|--------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEngine.SDK.Engine.Objects`     | `CEObject`, `ICEObject<TSelf>`                                                                                                                 | Borrowed handle: the native object pointer, equal by identity, with property, index and method access                                                                |
| `CheatEngine.SDK.Engine.Objects`     | `Owned<T>`                                                                                                                                     | Ownership of an object the plugin created; `Dispose` destroys it                                                                                                     |
| `CheatEngine.SDK.Engine.Values`      | `Address`                                                                                                                                      | An address read from a Lua integer or hexadecimal text; its own Lua marshaller                                                                                       |
| `CheatEngine.SDK.Engine.Values`      | `IndexBase`, `LuaSequence`                                                                                                                     | Zero-based indices over Cheat Engine objects and Lua sequences                                                                                                       |
| `CheatEngine.SDK.Engine.Enums`       | Enums, `CEEnumNames`, `EnumMarshaller<TEnum>`                                                                                                  | Numeric constants, their Cheat Engine names, and Lua integer marshalling                                                                                             |
| `CheatEngine.SDK.Engine.Runtime`     | `RuntimeInfo` (SDK-produced or caller-supplied), `RuntimeCapabilities`, `CheatEngineHostObservation`, `TargetArchitectureObservation`, `CheatEngineOperatingSystem`, `TargetBackend` | Separate host, target ISA family, bitness, configured pointer size, ABI, OS, Android and backend facts; never inferred from one another |
| `CheatEngine.SDK.Engine.Memory`      | `TargetMemory`, `HostMemory`, `HostAddress`                                                                                                    | Separate target/CE-host scalar, bounded span, target-width pointer, string and byte-table access                                                                     |
| `CheatEngine.SDK.Engine.Inspection`  | `EngineInspection`                                                                                                                             | Copied modules, sections, symbols, address resolution and memory-region snapshots                                                                                    |
| `CheatEngine.SDK.Engine.Allocation`  | `TargetMemoryAllocator`, `AllocatedRegion`                                                                                                     | Explicit ownership for target allocation, via a reviewed binding seam                                                                                                |
| `CheatEngine.SDK.Engine.Assembly`    | `AutoAssemblerPatcher`, `AutoAssemblerPatch`, `InstructionProfiles`, `InstructionAssembler`, `InstructionDisassembler`, `InstructionNavigator` | Auto Assembler owns a single `disableInfo`; separately, bounded profile-qualified Lua instruction operations return copied values and structured outcomes            |
| `CheatEngine.SDK.Engine.Scanning`    | `AobScanner`, `StringList`, `MemoryScanSession`                                                                                                | AOB result ownership and conservative MemScan/FoundList state transitions                                                                                            |
| `CheatEngine.SDK.Engine.AddressList` | `AddressListMutations`, `MemoryRecordId`                                                                                                       | ID-addressed record commands; borrowed GUI views never become managed owners                                                                                         |
| `CheatEngine.SDK.Engine.Errors`      | `EngineException` hierarchy, `EngineResourceHandoffException`                                                                                  | Stable distinction between expected CE, unavailable global, Lua, binding and marshalling failures; post-effect ownership publication reports its one cleanup attempt |
| `CheatEngine.SDK.Engine.Generated`   | `MemoryScalars`, `RuntimeCapabilityProbes`                                                                                                     | Generated wrappers: the earlier scalar memory contract, and raw read-only CE 7.7 runtime facts (ce77 spec)                                                          |

The CE 7.7 vertical slices add the following public domains. They use the same protected Lua boundary, but their
evidence and availability are intentionally separate: a catalogued Lua name is not a guarantee that every later CE
host has the same contract.

| Namespace                  | Public surface                                                                                                                                                | Boundary and result contract                                                                                                                                                                                                                                                                    |
|----------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Runtime`                  | `RuntimeInfo`, `RuntimeCapabilities`, `RuntimeCapabilityContract`, `CheatEngineHostObservation`, `TargetArchitectureObservation`, `TargetBackend`; produced by `Processes.RuntimeObservations` | An immutable snapshot of explicitly observed version, architecture, configured pointer size, backend and availability facts. Unknown remains unknown; an available global does not fill an unobserved ownership, thread or return field, and no fact is derived from another (see [Runtime facts and target backends](#runtime-facts-and-target-backends)). |
| `Memory`                   | `TargetMemory`, `HostMemory`, `Address`, `HostAddress`, `PointerSize`, `MemoryAccessFailure`                                                                  | Keeps attached-target addresses distinct from CE-host addresses. Target-width pointer, scalar, bounded-span, and string calls report expected CE/binding/Lua/result failures through `Try*` results; they do not claim a universal GUI-thread rule.                                             |
| `Inspection`               | `EngineInspection` and module, section, symbol and region value types                                                                                         | Returns copied managed snapshots. `NotFound` is used only where the CE 7.7 Lua contract documents `nil`; malformed data and Lua failures remain distinct status values.                                                                                                                         |
| `Allocation`               | `TargetMemoryAllocator`, `AllocatedRegion`                                                                                                                    | Models one target allocation as an explicit, single-use owner. It does not infer a GUI-thread requirement from an unspecific CE global, and reports a failed post-effect owner handoff with its one compensation outcome.                                                                       |
| `Objects` / `Scanning.Aob` | `StringList`, `StringLists`, `AobScanner`                                                                                                                     | `StringLists.TryCreate` and a successful `AobScanner.TryScan` out value yield `Owned<StringList>` only after a host object is returned. A list borrowed from CE must never be wrapped or destroyed by plugin code.                                                                              |
| `Scanning.Values`          | `MemScan`, `FoundList`, `MemoryScanSessions`, `MemoryScanSession`, scan requests and states                                                                   | The factory creates and owns the scanner/child pair, retains rollback authority through publication, and the session serializes documented state transitions and releases the child before the parent. It is explicitly main-thread-only; the generic `Owned<T>` wrapper is not.                |
| `AddressList`              | `AddressListAccess`, `AddressList`, `MemoryRecord`, `MemoryRecordId`, `AddressListMutations`                                                                  | The current GUI list and records are borrowed CE-owned handles. Mutations resolve IDs inside one protected command and report completed, not-started, or indeterminate effect; they do not promise historic record identity or a runtime-enforceable GUI-thread guard.                          |
| `Assembly`                 | `InstructionTargetProfile`, `InstructionAssembler`, `InstructionAssembly`, `AssemblePreference`, `InstructionDisassembler`, `InstructionNavigator`, `InstructionDisassembly`, `InstructionOperationStatus` | `InstructionProfiles` observes PID/probe/PID under one Lua admission and maps CE's x86 family plus 64-bit flag to x64. Each instruction call validates target width and rechecks that PID before and after CE's ambient operation; its result is copied and bounded, and the detailed assembler overload echoes the origin, jump preference and range-check option it sent. That coherence check is not a target lock or a live-host qualification. |
| `Errors`                   | `EngineException` and stable subclasses                                                                                                                       | Separates expected operation failure, global absence, Lua failure, binding violation and marshalling violation instead of exposing a raw Lua stack error as the public Engine contract.                                                                                                         |

The per-capability provenance, minimum CE version, architecture, thread, ownership and return semantics are tracked
against the audit's Lua surface register; executed host evidence is a separate, qualified claim. Fixture tests
validate managed behavior and the pinned Lua fixture; opt-in live evidence is recorded separately and is not implied
by these wrappers.

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

`RuntimeInfo` is an immutable snapshot, produced by `RuntimeObservations.TryObserveRuntimeInfo` or supplied by an
integration layer through its legacy constructor. The SDK-produced snapshot carries a `CheatEngineHostObservation` and,
when a target is selected, a `TargetArchitectureObservation`; its `PointerSize` is Cheat Engine's configured pointer size
(`getPointerSize`), not the target bitness. It deliberately does not turn a legacy floating-point `getCEVersion` result
into a complete file version (it reads `getCheatEngineFileVersion` instead) and does not infer target architecture,
pointer width, ABI, ownership, return semantics or thread affinity. `RuntimeCapabilities` records each probed capability
as available or unavailable with the evidence fields that are actually known; a capability that was not probed stays
unknown.

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
incarnation combines the selected PID read from `getOpenedProcessID` with the local process creation time, and only
when `isConnectedToCEServer()` returned `false` in the same operation; missing, malformed, inaccessible and no-target
facts remain explicit observations instead of fabricated identities. A CEServer connection, an absent backend probe
and the file-as-process sentinel PID are refused with their own statuses and never produce local creation-time evidence,
so target-bound owners refuse them (see the backend table below). It neither opens nor selects a process. It can only
compare observations: no inspected CE primitive makes an observation atomic with a following ambient-target Lua
effect, so an external selection transition in that interval, including an unseen A→B→A sequence, remains unqualified.

`InstructionProfiles.TryObserveCurrent` has a narrower purpose than process qualification: it reads CE's selected PID,
the documented target ISA probes, and the PID again to construct an `InstructionTargetProfile`. On Cheat Engine an x64
target is the x86 family plus the 64-bit flag (`targetIsX86() == true` and `targetIs64Bit() == true`), so the profile
follows `RuntimeInfo.TryDeriveTargetArchitecture` (see [Runtime facts and target backends](#runtime-facts-and-target-backends)):
x86 family gives X64 or X86, ARM family gives Arm64 or Arm32, and both families or neither is `InvalidProfile`. It never
uses the managed host width or CE's configured pointer size. With no target selected CE reports x64-like facts, so a
zero PID is `TargetNotSelected` before any probe; the file-as-process sentinel PID is `UnsupportedTargetBackend`.
`InstructionAssembler` always sends its
explicit `Address` to CE as the relative-operand origin, validates the complete returned byte table, and copies no
prefix on a malformed result, short destination, or observed target change. Its detailed overload also sends an
`AssemblePreference` (CE's `apNone`/`apShort`/`apLong`/`apFar`) and the `skipRangeCheck` option, always as four
arguments, and returns an `InstructionAssembly` echo of the target, profile, origin, preference and option; the legacy
overload still sends exactly two arguments. `nil` with or without a message is `InstructionRejected` and the message
is never read. `InstructionDisassembler` bounds and copies
the raw UTF-8 display line before resolving the split helper, bounds all four raw split fields before decoding them,
then returns managed strings; consumers never need to parse that UI text. `InstructionNavigator.TryGetPrevious` retains
CE's documented estimate semantics. These mappings use protected Lua globals only. The historical classic `Assembler`,
`Disassembler`, `disassembleEx`, `previousOpcode`,
and `nextOpcode` slots remain unprojected because their reviewed ABI and output-capacity evidence is conflicting or
insufficient; their buffer-based forms are not applicable to the managed-hostfxr profile (audit A15-19). Fixture tests verify these managed contracts; no test in this repository qualifies a live CE target,
target architecture, selection lock, relocation backend, or plugin Native AOT loading.

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
successful-list outcomes; `TryScan` keeps its compatible `bool` projection and supplies a caller-owned
`Owned<StringList>` through its `out` parameter. A valid empty list is still a successful caller-owned result, not a
match classification. `AobScanner.TryScanOutcome` adds the factual distinction that a valid `StringList` with a
verified zero count is `NoMatches`; raw `nil` remains a distinct result, never a no-match inference. It retains the
caller-owned list for both `Matches` and `NoMatches`, reports an unreadable or negative count as a separate outcome
after disposing that otherwise unreturnable owner, and preserves the protected Lua status without copying transient
error text. The CE call is synchronous: this SDK exposes no range, module, result-limit, early-stop or
`CancellationToken` control because none is established for this `AOBScan` path. Client code may cap its own copied
data after the full host list returns, but that does not bound or interrupt CE work. CE documents an AOB result list as
caller-freed; `StringList` itself remains a borrowed handle. `MemScan` and `FoundList` are borrowed handle values, while
`MemoryScanSessions.TryCreate` is the SDK's source-backed CE 7.7 creation path: it immediately owns the returned parent
and child, holds one Lua operation across both calls, retains raw handles until their owner is published, rolls the
child back before its parent on every later failure, and transfers the pair only to `MemoryScanSession`; an ordinary
consumer cannot create an `Owned<MemScan>` or `Owned<FoundList>` manually. `AutoAssemblerPatcher` retains the returned
disable-info table while it is rooted and performs one target-qualified disable if tracking or patch publication fails;
the same `EngineResourceHandoffException` reports whether that compensation was confirmed, safely refused, or uncertain.
`AutoAssemblerPatch.ReleaseWithTargetOutcome` is the additive structured view of its one cleanup attempt: it reports
confirmed disable, a target-qualified refusal, cleanup that could not begin, or an attempted unconfirmed effect without
deriving a new cleanup action from the script or an address. It still rejects a second release because its sole owner
has been consumed. The patch never
reselects a target, and an ambient target transition between an observation and a Lua effect remains an explicit
host-contract gap rather than a claim that the SDK can lock Cheat Engine's selection.
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

`AddressListMutations` is the typed mutation boundary for those GUI-owned records. Its `Delete` and `SetParent`
commands take only `MemoryRecordId` values (and an explicit hierarchy traversal bound), resolve against the current
address list in one admitted Lua operation, and return a structured effect. A protected failure after `destroy()` or
the `Parent` setter starts is intentionally **indeterminate**, not a retry-safe failure. The command result contains no
snapshot: a consumer that needs one must read a new view after a completed result.

`SymbolRegistry.TryRegisterOwned` returns `SymbolRegistrationLease`, an explicit cleanup coordinator rather than an
exclusive CE owner. CE supplies no registration token and unregisters by name only, so the lease prevents an older lease
from deleting a newer registration made through the same SDK coordinator; it cannot detect a replacement by external
Lua, another plugin, or another SDK copy. On runtime epoch or state-generation change it sends no unregister to the new
universe.

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

### Runtime facts and target backends

Cheat Engine reports each runtime fact through its own Lua global. The SDK reads each one separately, keeps an absent
global as `null` or `Unknown` (never `false`), and never derives one fact from another. Every observation of a target
reads `getOpenedProcessID` first and last, and reads no target fact when it is 0.

| Fact                                 | CE global                   | SDK member                                                                                           | When unknown                                  |
|--------------------------------------|-----------------------------|------------------------------------------------------------------------------------------------------|-----------------------------------------------|
| CE file version                      | `getCheatEngineFileVersion` | `RuntimeHostOperations.TryGetCheatEngineFileVersion`, `CheatEngineHostObservation.FileVersion`       | absent global or no value returned            |
| CE host architecture                 | `getSystemArchitecture`     | `RuntimeHostOperations.TryGetSystemArchitecture`, `CheatEngineHostObservation.SystemArchitecture`   | absent global                                 |
| CE is 64-bit                         | `cheatEngineIs64Bit`        | `RuntimeHostOperations.TryIsCheatEngine64Bit`, `CheatEngineHostObservation.CheatEngineIs64Bit`       | absent global; never taken from the host arch |
| CE operating system                  | `getOperatingSystem`        | `RuntimeHostOperations.TryGetOperatingSystem`, `CheatEngineHostObservation.OperatingSystem`         | absent global                                 |
| Selected process                     | `getOpenedProcessID`        | `TargetArchitectureObservation.ProcessId`                                                            | 0: no target, no fact is read                 |
| Target backend                       | `isConnectedToCEServer`     | `TargetArchitectureObservation.Backend`, `TargetSelectionObservation.Backend`                        | absent global: `Unknown`                      |
| Target bitness (CE's 64-bit flag)    | `targetIs64Bit`             | `TargetArchitectureObservation.Bitness`, `CurrentProcessObservation.PointerSize`                     | required by the target observation            |
| Target ISA family                    | `targetIsX86`, `targetIsArm` | `TargetArchitectureObservation.IsX86Family`, `.IsArmFamily`, `.Architecture`                        | absent global: `null`, architecture `Unknown` |
| Android target                       | `targetIsAndroid`           | `TargetArchitectureObservation.IsAndroid`                                                            | absent global: `null`                         |
| Target ABI                           | `getABI`                    | `TargetArchitectureObservation.AbiCode`, `.Abi`                                                      | absent global, or an undocumented code (kept raw) |
| CE's configured pointer size         | `getPointerSize`            | `RuntimeProcessOperations.TryGetConfiguredPointerSize`, `TargetArchitectureObservation.ConfiguredPointerSize`, `RuntimeInfo.PointerSize` | absent global, or any value other than 4 or 8 (kept raw) |

`RuntimeObservations.TryObserveRuntimeInfo` reads all of them in one admission and produces a `RuntimeInfo`;
`RuntimeCapabilityProbes` (generated from the `ce77` runtime spec) exposes the raw values for callers that need them. None
of these calls `setPointerSize`, `setAssemblerMode`, `openProcess`, `openFileAsProcess` or a `dbk_*`/`dbvm_*` global: a
runtime query loads no driver and changes no target (audit A17-18, Q45).

The ISA family is reported separately from the 64-bit flag, and the SDK derives an architecture only from both
families plus the flag, through the pure `RuntimeInfo.TryDeriveTargetArchitecture`:

| `targetIsX86` | `targetIsArm` | Architecture                            |
|---------------|---------------|-----------------------------------------|
| true          | false         | `targetIs64Bit` ? `X64` : `X86`         |
| false         | true          | `targetIs64Bit` ? `Arm64` : `Arm32`     |
| true          | true          | `Unknown` (instruction profile refused) |
| false         | false         | `Unknown` (instruction profile refused) |
| absent        | any           | `Unknown` (instruction profile refused) |

Evidence: CE 7.7.0.10621 x64 reported `targetIsX86() == true` and `targetIs64Bit() == true` for an x64 target and
`targetIsX86() == true`, `targetIs64Bit() == false` for an x86 target (spike C3 D2: a Lua-only host observation used as
a design input, not a qualification). The public CE source at `ec45d5f` agrees (`ProcessHandlerUnit.pas:24`,
`:115-138`). With no target selected CE also reports the x86 family, the 64-bit flag and an 8-byte pointer size, which
is why the process identifier comes first.

The configured pointer size is not the bitness. On the same host, `setPointerSize(4)` on an x64 target made
`getPointerSize()` return 4 while `targetIs64Bit()` stayed true and `readPointer` kept reading 8 bytes;
`setPointerSize(2)` was accepted, and selecting the target again reset the value (spike C3 D3). The SDK therefore reports
both, keeps any raw configured value, never calls `setPointerSize`, and never takes a target width from `IntPtr.Size`.
`PointerSize.FromArchitecture` is obsolete ([CESDK7001](../../analyzers/docs/CESDK7001.md)). The little-endian pointer
encoding of `PointerSize.TryReadLittleEndian`/`TryWriteLittleEndian` is an assumption of the local x86/x64 profile, not a
general Cheat Engine fact (audit A12-04).

Target backends produce different evidence and are separate profiles (audit A12-03, ADR-11):

| Backend         | How the SDK recognises it                                                        | Incarnation evidence  | Qualified in the support profile                   |
|-----------------|----------------------------------------------------------------------------------|-----------------------|----------------------------------------------------|
| `LocalProcess`  | `isConnectedToCEServer() == false` and a PID in (0, `int.MaxValue`]               | PID + local StartTime | yes, `ce-7.7.0.10621-x64-managed-hostfxr`          |
| `FileAsProcess` | sentinel PID 4294967295 (CE source `ec45d5f`, ObservedSource; ToQualify on 7.7)  | none                  | no                                                 |
| `CEServer`      | `isConnectedToCEServer() == true`                                                | none                  | no                                                 |
| `Unknown`       | `isConnectedToCEServer` absent                                                   | none                  | no                                                 |

A file opened as a process has no operating-system process: the SDK refuses it and never searches for a Windows process
(A12-07). A local BCL PID and creation time do not describe a PID served remotely by CEServer (A12-05). Cheat Engine's
target stays ambient: the user, another plugin or a script can switch it at any time, and the SDK's before-and-after
checks reduce that risk without making an operation a transaction or a lock (A12-01).

These behaviours are proven by fixture tests (C1/C2) only; host-level evidence belongs to the
[qualification matrix](../../docs/qualification/README.md) (Q30, Q31, Q32, Q45).

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
10. Runtime facts remain explicit and unknown fields stay unknown; capability observations are immutable copies; the
    ISA family, bitness, configured pointer size, ABI, Android, host and backend facts are read separately and never
    derived from one another (`RuntimeContractsTests`, `RuntimeProcessOperationsTests`, `RuntimeHostOperationsTests`,
    `RuntimeObservationsTests`, `RuntimeCapabilityProbesTests`).
11. Target and host memory cannot cross address spaces implicitly; scalar, span, text and failure paths keep order and
    restore their Lua stack (`MemoryApiTests`).
12. Inspection publishes complete snapshots only, and distinguishes `nil`, malformed result, unavailable global and Lua
    failure (`EngineInspectionTests`).
13. Allocation ownership is consumed once, AOB lists are owned deterministically, and the MemScan/FoundList state
    machine
    destroys its child before its parent (`AllocatedRegionTests`, `AobScannerTests`, `MemoryScanSessionTests`).
14. Address-list and memory-record wrappers remain borrowed and intentionally do not assert an unproven main-thread
    contract; ID-addressed mutations validate hierarchy and preserve indeterminate host effects
    (`AddressListValueTests`, `AddressListLuaTests`, `AddressListMutationsTests`).
15. Runtime metadata preserves unknown fields; an SDK-produced `RuntimeInfo` reports CE's configured pointer size and
    lists only probed capabilities; runtime probes call only read-only globals; target and host address spaces cannot be
    mixed; expected memory failures do not become exceptions (`RuntimeContractsTests`, `RuntimeObservationsTests`,
    `MemoryApiTests`).
16. Module, section, symbol and region calls distinguish documented `nil` from Lua/binding/malformed-result failures
    and never publish a partial copied destination (`EngineInspectionTests`).
17. Allocation, AOB, StringList, scan-session and address-list tests exercise ownership transfer, zero-based access,
    deterministic child-before-parent cleanup, forbidden scan state transitions, and coordinator-qualified symbol
    cleanup (`AllocatedRegionTests`, `AobScannerTests`, `StringListTests`, `MemoryScanSessionTests`,
    `AddressListLuaTests`, `AddressListMutationsTests`, `SymbolRegistryTests`). These are fixture contracts,
    not a substitute for a controlled CE 7.7 live run.
18. Instruction profiles map CE's x86 family plus 64-bit flag to x64 and refuse contradictory, absent, no-target and
    file-as-process facts; the assembler echoes the origin, preference and range-check option it sent; target selection
    refuses local incarnation evidence for CEServer, unknown-backend and file-as-process targets
    (`InstructionOperationsTests`, `InstructionAssemblerTests`, `RuntimeProcessOperationsTests`,
    `RuntimeObservationsTests`, `TargetSelectionTests`, `RuntimeCapabilityProbesTests`). These are C1/C2 fixture
    contracts, not host qualification.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Engine.Tests -c Debug --fail-skips on
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
the [test project README](../../tests/CheatEngine.SDK.Engine.Tests/README.md).
