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
| `CheatEngine.SDK.Engine.Objects`     | `Owned<T>`, `EngineResourceOrigin`, `EngineEffectState`                                                                                        | Ownership of an object the plugin created; `Dispose` destroys it once, never in another Lua universe; origin and effect vocabulary shared by every durable resource  |
| `CheatEngine.SDK.Engine.Values`      | `Address`                                                                                                                                      | An address read from a Lua integer or hexadecimal text; its own Lua marshaller                                                                                       |
| `CheatEngine.SDK.Engine.Values`      | `IndexBase`, `LuaSequence`                                                                                                                     | Zero-based indices over Cheat Engine objects and Lua sequences                                                                                                       |
| `CheatEngine.SDK.Engine.Enums`       | Enums, `CEEnumNames`, `EnumMarshaller<TEnum>`                                                                                                  | Numeric constants, their Cheat Engine names, and Lua integer marshalling                                                                                             |
| `CheatEngine.SDK.Engine.Runtime`     | `RuntimeInfo` (SDK-produced or caller-supplied), `RuntimeCapabilities`, `CheatEngineHostObservation`, `TargetArchitectureObservation`, `CheatEngineOperatingSystem`, `TargetBackend` | Separate host, target ISA family, bitness, configured pointer size, ABI, OS, Android and backend facts; never inferred from one another |
| `CheatEngine.SDK.Engine.Memory`      | `TargetMemory`, `HostMemory`, `HostAddress`                                                                                                    | Separate target/CE-host scalar, bounded span, target-width pointer, string and byte-table access                                                                     |
| `CheatEngine.SDK.Engine.Inspection`  | `EngineInspection`, `SymbolRegistry`, `SymbolLists`, `SymbolList`                                                                              | Copied modules, sections, symbols, address resolution and memory-region snapshots; symbol registration leases and plugin-owned symbol lists                         |
| `CheatEngine.SDK.Engine.Allocation`  | `TargetMemoryAllocator`, `AllocatedRegion`, `TargetAllocationAcquireOutcome`                                                                   | Explicit ownership for target allocation, via a reviewed binding seam; never a live address without an owner or a reported compensation                             |
| `CheatEngine.SDK.Engine.Assembly`    | `AutoAssemblerPatcher`, `AutoAssemblerPatch`, `InstructionProfiles`, `InstructionAssembler`, `InstructionDisassembler`, `InstructionNavigator` | Auto Assembler owns a single `disableInfo`; separately, bounded profile-qualified Lua instruction operations return copied values and structured outcomes            |
| `CheatEngine.SDK.Engine.Scanning`    | `AobScanner`, `StringList`, `MemoryScanSession`                                                                                                | AOB result ownership and conservative MemScan/FoundList state transitions                                                                                            |
| `CheatEngine.SDK.Engine.AddressList` | `AddressListMutations`, `MemoryRecordId`, `MemoryRecordActivationOutcome`                                                                      | ID-addressed record commands, including activation with its real effect; borrowed GUI views never become managed owners                                             |
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
| `Allocation`               | `TargetMemoryAllocator`, `AllocatedRegion`, `TargetAllocationAcquireOutcome`                                                                                  | Models one target allocation as an explicit, single-use owner bound to its Lua runtime identity and target incarnation. It does not infer a GUI-thread requirement from an unspecific CE global, and reports a failed post-effect owner handoff with its one compensation outcome.              |
| `Objects` / `Scanning.Aob` | `StringList`, `StringLists`, `AobScanner`                                                                                                                     | `StringLists.TryCreate` and a successful `AobScanner.TryScan` out value yield `Owned<StringList>` only after a host object is returned. A list borrowed from CE must never be wrapped or destroyed by plugin code.                                                                              |
| `Scanning.Values`          | `MemScan`, `FoundList`, `MemoryScanSessions`, `MemoryScanSession`, scan requests and states                                                                   | The factory creates and owns the scanner/child pair, retains rollback authority through publication, and the session serializes documented state transitions and releases the child before the parent. It is explicitly main-thread-only; the generic `Owned<T>` wrapper is not.                |
| `AddressList`              | `AddressListAccess`, `AddressList`, `MemoryRecord`, `MemoryRecordId`, `AddressListMutations`                                                                  | The current GUI list and records are borrowed CE-owned handles. Mutations resolve IDs inside one protected command and report completed, not-started, or indeterminate effect (activation: before/after state, host refusal, pending); they do not promise historic record identity.            |
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
evidence-backed thread rule. There is no finalizer. An owner records the Lua runtime identity (attach epoch and state
generation) that created it as its `Origin`, and a transfer keeps it. It never destroys in another Lua universe: after a
re-enable or a controlled state replacement, `Dispose` consumes the owner without any Cheat Engine call and
`LastReleaseOutcome` reports `RefusedRuntimeChanged`; `TryDestroy` does the same and throws. Dispose every owner while
the plugin is still enabled: while detached, `Dispose` and `TryDestroy` throw and retain the owner (a later re-enable
makes it stale, so the next attempt consumes it without a call). `ReleaseWithOutcome()` never throws and always consumes
the owner: `Released`, `UnconfirmedAfterInvocation` when `destroy()` raised (for example because a parent destroyed the
object first), `RefusedRuntimeChanged`, or `NotInvoked` when no call could begin. A destroy is never retried.
`Abandon()` gives up managed cleanup without granting another caller permission to create an owner. `Value` stays a
borrowed view: check `Origin.IsCurrentRuntime` before using a handle kept across a re-enable.

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

`TargetMemoryAllocator` uses the production `allocateMemory`/`deAlloc` binding by default and retains
`ITargetMemoryAllocationOperations` as the direct compatibility seam. The production binding preserves CE's optional
target address and page-protection positions; it is internal, because its direct members would yield a live address
without an owner. To create an `AllocatedRegion`, an implementation must additionally opt into
`ITargetBoundMemoryAllocationOperations`, which captures a qualified incarnation and refuses a later observed mismatch
without selecting a replacement target. A legacy direct seam remains usable for its original ambient-target operations,
but is not silently converted into an owner with an unverified cleanup target.

`Allocate` keeps its throwing contract. `TryAllocate(request, out AllocatedRegion? region)` reports every result as a
`TargetAllocationAcquireOutcome` instead: the CE call result (its address stays readable whenever CE returned one), an
`EngineEffectState` (`NotStarted` before any call, `NotApplied` for CE's documented `nil`, `Applied` when CE returned an
address, `Unknown` for a protected failure or a malformed result), the target observation, and `HasOwner`. When CE
allocated but no owner can be published (an unqualified target, a Lua runtime identity change during the call, or a
failed owner publication), it makes exactly one target-qualified compensation and reports it as `Compensation`, so an
applied allocation never leaves without an owner or a compensation result.

An allocation is bound to the runtime identity and target incarnation that created it (`AllocatedRegion.Origin`). After a
re-enable or a controlled state replacement the region refuses cleanup without any call (`RefusedRuntimeChanged`) and
reports the residue; after a detach, cleanup cannot begin (`NotInvoked`). It never reopens or reselects a target.
`Dispose` and `ReleaseWithTargetOutcome` never throw once ownership is taken; `Release` and `ReleaseWithOutcome` throw a
lifecycle `InvalidOperationException` for those two cases. Every release path consumes ownership first, so a
potentially partial deallocation is never retried, and a released region can never free a later allocation that reuses
its address. CE 7.7 has no documented separate post-allocation protection call in this surface. If CE accepts an
allocation but managed owner publication then throws, `Allocate` attempts the same target-qualified deallocation once
and throws `EngineResourceHandoffException`; its `CleanupOutcome` distinguishes a confirmed release, a safe refusal,
cleanup that could not begin, and an effect requiring manual recovery.

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
consumer cannot create an `Owned<MemScan>` or `Owned<FoundList>` manually.
The Client must still keep value scanning capability-gated until its opt-in CE 7.7 x64 live scenario validates creation,
cleanup, disable/re-enable, and target changes. The session guards the `firstScan → waitTillDone → initialize → read →
deinitialize` order and rejects worker-thread cleanup while attached because its owned children use the existing SDK
owner destruction contract. That conservative SDK guard is not evidence that CE's catalog itself declares every scan
operation main-thread-only. The raw `MemScan` and `FoundList` handles likewise carry no `MainThreadOnly` metadata before
a live probe establishes one.

`AutoAssemblerPatcher` calls `autoAssemble(script)` with three results (success, disable information or error detail,
compilation warnings) and never passes `targetself`. `TryApplyWithOutcome` reports an `AutoAssemblerApplyOutcome`
instead of a Boolean: `Applied`, `AppliedTargetChanged` (CE applied the script but the captured target no longer
matched right afterwards; the patch is still published with its original incarnation, and its release refuses the
other target), `Rejected`, `GlobalUnavailable`, `ProtectedLuaFailure`, `InvalidResult`, `TargetIdentityUnavailable` or
`HandoffFailed` (CE applied the script but the disable information could not be rooted, copied or handed to a patch; the
one target-qualified compensating disable is the outcome's `Compensation`). Its `EngineEffectState` is `Applied` only
for `Applied`, `NotStarted` for an unavailable global or target, and `Unknown` otherwise: a rejection by CE does not
prove that nothing changed. CE gives no factual signal that separates a syntax error, an unresolved symbol, an
impossible allocation, a failed include or a failed DLL injection, so these are all `Rejected`: partially
distinguishable by design. No category is ever derived from CE's text. With `AutoAssemblerOptions.CaptureHostText`
(off by default, because the text can contain script source and paths) the outcome also carries CE's rejection detail
and warnings, copied bounded to `MaxHostTextBytes` UTF-8 bytes and cut at a scalar boundary; `HasHostWarnings` is
reported whether or not the text was captured.

Every applied patch carries an `AutoAssemblerDisableInfoSnapshot`: a bounded copy of the `allocs`, `registeredsymbols`,
`exceptionlist` and `symbols` sections (dictionaries sorted by ordinal name, sequences in CE's order, addresses above
2^63 exact) and whether a `ccodesymbols` list exists. That list belongs to CE: the SDK never owns, destroys or
unregisters it. The snapshot is diagnostic data. A malformed or truncated snapshot (status `Malformed` or `Truncated`)
never fails the activation: the rooted table, passed back unchanged to CE's `[DISABLE]` handling, stays the only disable
authority, and no `[DISABLE]` script is ever rebuilt from saved bytes. The patch exposes `Origin` (runtime identity and
incarnation), `DisableInfo` and `PostApplyTargetCheck`. `Dispose`, `Release` and `ReleaseWithTargetOutcome` consume the
owner before one CE disable and never repeat it; after a re-enable or a controlled state replacement they report
`RefusedRuntimeChanged` without a call. No rollback stronger than CE's own is promised: a disable that returns `false`
or raises leaves `RequiresManualRecovery` set. `TryCheck(script, enable)` calls `autoAssembleCheck` with exactly the
script and the enable flag and never creates an owner; a syntax check is not proof that the activation will succeed.
Both globals are resolved through the cached protected global push. `Apply` and `TryApply` keep their compatible
contract and throw `EngineResourceHandoffException` for a failed handoff, with the same compensation outcome. The patch
never reselects a target, and an ambient target transition between an observation and a Lua effect remains an explicit
host-contract gap rather than a claim that the SDK can lock Cheat Engine's selection.

`AddressList` and `MemoryRecord` are always borrowed GUI handles, including a record created by
`AddressList.TryCreateMemoryRecord`, because CE adds it to the address list. Their possible GUI affinity is documented
as an inference only: they deliberately have no `MainThreadOnly` metadata until the opt-in CE 7.7 dispatcher probe
establishes an enforceable host contract.

`AddressListMutations` is the typed mutation boundary for those GUI-owned records. Its `Delete`, `SetParent` and
`SetActive` commands take only `MemoryRecordId` values (and an explicit hierarchy traversal bound), resolve against the
current address list in one admitted Lua operation, and return a structured effect. A protected failure after
`destroy()` or the `Parent` setter starts is intentionally **indeterminate**, not a retry-safe failure; a second delete
of the same identifier reports `RecordNotFound` without a second destroy. The command result contains no snapshot: a
consumer that needs one must read a new view after a completed result. Every command is refused with
`TableLoadInProgress`, before any Lua call, while `CheatTableFiles.TryLoad` runs on the same thread (for example from a
script of the table being loaded).

`SetActive(MemoryRecordId, bool)` reads `Active`, does not call the setter when the record already has the requested
state (`Unchanged`), calls the setter once, then reads `Active` and `AsyncProcessing` back: `Applied`, `RefusedByHost`
(an `OnActivate` returning false or a failed `[ENABLE]`, which may have applied part of its effects), `Pending` (an
asynchronous record still processing), or `Indeterminate` (the setter raised after it started, or the read-back
failed). It never retries: a third-party `OnActivationFailure` handler that asks CE to retry can make CE loop inside the
one setter call, which the SDK cannot prevent. `MemoryRecord` adds reads of `Active`, `Async`, `AsyncProcessing`,
`Script` and `OffsetCount`, and deliberately has no `Active` setter on the borrowed handle.

`CheatTableFiles` projects only the file overloads `loadTable(path, merge)` and `saveTable(path)`, with exactly those
arguments. The file overload has no option to suppress CE's Lua-script dialog, so a table that contains scripts may
prompt or execute Lua. The stream overloads of `loadTable`/`saveTable` and the `protect`/`dontDeactivateDesignerForms`
options of `saveTable` are not projected until a CE stream projection exists; the Lua surface catalogue records them as
deferred. The SDK applies no path policy: the application's trust policy owns which tables may be loaded, and a file
must never be turned into a stream to bypass it.

`SymbolRegistry.TryRegisterOwned` returns `SymbolRegistrationLease`, an explicit cleanup capability rather than an
exclusive CE owner. CE supplies no registration token and unregisters by name only. The lease records the registered
`Address` and its `Origin`; before its one unregister it resolves the name again: when the name no longer resolves it
reports `ExternallyRemoved`, and when it resolves to another address it reports `Replaced`; neither sends an unregister,
so a newer third-party definition is never removed. The check is best effort and not atomic (a third party can replace
the name between the lookup and the unregister), and a name that parses as an expression or collides
case-insensitively can produce a conservative `Replaced`. A failed lookup keeps the lease retryable
(`CleanupUnavailable`). The lease also never unregisters a newer registration made through the same SDK coordinator,
and on a runtime epoch or state-generation change it sends nothing to the new universe. Registration keeps CE's own
behavior for an existing name; a consumer that needs a collision policy resolves the name first with
`EngineInspection.ResolveAddress`. CE's `deleteAllRegisteredSymbols` is deliberately not bound: it removes the symbols
of every script, table and plugin, so it is never a per-plugin cleanup path (a repository test keeps it out of the
shipping sources).

`SymbolLists` keeps three things distinct: a symbol registered on its own, a symbol list owned by the plugin, and the
main list borrowed from CE. `TryCreate` binds the zero-argument `createSymbolList()` only (its initial-list overload
registers automatically) and returns an `Owned<SymbolList>`; `TryGetMain` returns the main list as a borrowed
`SymbolList` that no SDK API can register, unregister or destroy. `TryRegister` transfers the owned list into a
`SymbolListRegistrationLease` once `register()` began (unconfirmed when it raised), and the lease unregisters before it
destroys: an `unregister()` that raised abandons the list without destroying it, because destroying a possibly
registered list could leave a dangling entry in CE's symbol handler. The borrowed `SymbolList` members (`clear`,
`addSymbol` with exactly four arguments, `deleteSymbol`, `getSymbolFromAddress`, `getSymbolFromString`, `Name`, `PID`)
return a `LuaOperationStatus` that separates a missing member, a Lua failure, a not-found `nil` and a malformed symbol
table.

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
reads `getOpenedProcessID` first, and reads no target fact when it is 0 or the file-as-process sentinel. The fact
observations `RuntimeProcessOperations.ObserveTargetArchitecture` and `TryGetConfiguredPointerSize`,
`RuntimeObservations.TryObserveRuntimeInfo` and `InstructionProfiles.TryObserveCurrent` also read it last and report a
difference as `TargetChanged`; instruction operations re-check it before and after their Cheat Engine call.
`TargetSelection.ObserveCurrent` and `RuntimeProcessOperations.ObserveCurrent` read it once, before their other probes,
and `RuntimeProcessOperations.SelectAndObserve` reads it once, after `openProcess`, to confirm the selection.

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
`RuntimeCapabilityProbes` (generated from the `ce77` runtime spec) exposes the raw values for callers that need them.
Both are projections of the same globals. Their integer policies differ: a generated `int32` result goes through the
EngineApi marshaller, which also converts an integral float or an integer numeral string, while the members above
require the Lua integer that Cheat Engine pushes and report any other value as `InvalidResult`
(`generated_int32_probe_converts_an_integral_float_or_numeral_string_that_the_structured_api_refuses`). None
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

| Backend         | How the SDK recognises it                                                        | Incarnation evidence  | Qualified backend                                     |
|-----------------|----------------------------------------------------------------------------------|-----------------------|-------------------------------------------------------|
| `LocalProcess`  | `isConnectedToCEServer() == false` and a PID in (0, `int.MaxValue`]               | PID + local StartTime | yes, profile `ce-7.7.0.10621-x64-managed-hostfxr`     |
| `FileAsProcess` | sentinel PID 4294967295 (CE source `ec45d5f`, ObservedSource; ToQualify on 7.7)  | none                  | no                                                    |
| `CEServer`      | `isConnectedToCEServer() == true`                                                | none                  | no                                                    |
| `Unknown`       | `isConnectedToCEServer` absent                                                   | none                  | no                                                    |

A file opened as a process has no operating-system process: the SDK refuses it and never searches for a Windows process
(A12-07). A local BCL PID and creation time do not describe a PID served remotely by CEServer (A12-05). Cheat Engine's
target stays ambient: the user, another plugin or a script can switch it at any time, and the SDK's before-and-after
checks reduce that risk without making an operation a transaction or a lock (A12-01).

These behaviours are proven by fixture tests (C1/C2) only, never by a run on a Cheat Engine host (audit ADR-04): the
host-level (C3/C4) scenarios Q30, Q31, Q32 and Q45 are not executed.

## Ownership and origin (Checkpoint D)

Every durable resource knows the context that created it (`EngineResourceOrigin`: the Lua runtime identity, attach epoch
and state generation, compared together, and a target process incarnation for target-bound resources), its one release
action, the ways it can be destroyed behind the plugin's back, and what it does when the target or the Lua state
changed. The application chooses the policy; the SDK keeps native authority; a Client lease delegates to these owners
and never owns destroy itself. Only sourced SDK factories create owners, a getter never grants destroy authority, and
no finalizer ever repairs a forgotten cleanup. Effects use one vocabulary, `EngineEffectState` (`Unknown = 0`,
`NotStarted`, `NotApplied`, `Applied`).

| Resource | Origin captured | Release action | External-destruction risk | On target change | On Lua state change / re-enable | On detach | Retry policy |
|----------|-----------------|----------------|---------------------------|------------------|---------------------------------|-----------|--------------|
| `Owned<T>` (StringList, SymbolList, MemScan/FoundList inside a session) | Runtime identity, inside the creating factory's admitted operation; a transfer keeps it | One `destroy()` (`Dispose`, `TryDestroy`, `ReleaseWithOutcome`) | A parent or CE destroys the object first: the destroy raises, `UnconfirmedAfterInvocation` | Not target-bound | Consumed without a call, `RefusedRuntimeChanged` (residue) | `Dispose`/`TryDestroy` throw and retain; `ReleaseWithOutcome` consumes, `NotInvoked` | Never after a destroy began |
| `AllocatedRegion` | Runtime identity and qualified incarnation, around the allocating call | One target-validated `deAlloc` | The target exits or its PID is reused: refused; a later allocation at the same address is never freed through this owner | Refused (`RefusedTargetChanged`, `RefusedProcessReused`, `RefusedNoTarget`), residue reported, never reselects | Refused without a call, `RefusedRuntimeChanged` | `NotInvoked` | Never |
| `AutoAssemblerPatch` | Runtime identity and incarnation, validated again right after the effect | One `autoAssemble(script, disableInfo)` with the rooted table | A script or table disables it, or the target exits: the disable fails, manual recovery | Refused, token kept until consumed; a change during the effect is `AppliedTargetChanged` | Refused without a call, `RefusedRuntimeChanged`; the reference is dropped without touching the new state | `NotInvoked` | Never |
| `SymbolRegistrationLease` | Runtime identity and the registered address | Name lookup, then one `unregisterSymbol(name)` only if the name still maps to the address | A third party replaces or removes the name: `Replaced` / `ExternallyRemoved`, no unregister | Not target-bound (a lookup in another target reads as a conservative `Replaced`/`ExternallyRemoved`) | `StaleRuntime`, no call | `StaleRuntime`, no call | Only `CleanupUnavailable` (no call began) |
| `SymbolListRegistrationLease` | The list owner's runtime identity | `unregister()`, then the list's one `destroy()` | CE destroys the list: `unregister()` raises, `CleanupIndeterminate`, abandoned without destroy | Not target-bound | `StaleRuntime`, list abandoned without a call | `StaleRuntime`, list abandoned without a call | Only when `unregister` is unavailable |
| `MemoryScanSession` (S-SCAN, Wave 2) | `RuntimeIdentity` and target observation (an `Origin` property is added by S-SCAN) | FoundList `deinitialize`/`destroy`, then MemScan `destroy` | CE or a parent destroys a child: unconfirmed | Refused without cleanup | Consumed without cleanup | Consumed without cleanup | Never |
| `MemoryRecord` (address list) | None: a borrowed CE-owned record | None by the plugin; `AddressListMutations` commands resolve the record by identifier in each command | A table reload, a parent delete or CE removes it: `RecordNotFound` | Not target-bound | Commands resolve again; an identifier is never a historic identity | Commands throw (lifecycle) | Never; a second delete reports `RecordNotFound` |
| Timers, hotkeys, lookup callbacks | Placeholder: S-EVT, Wave 3 | | | | | | |

An external `resetLuaState` that bypasses the SDK's controlled replacement is not detected today: the state generation
does not advance, so this policy does not trigger; detection is a Lua runtime concern. Every row is a fixture contract
(managed doubles and the bundled Lua), not host qualification; the host-level scenarios stay in the
[qualification matrix](../../docs/qualification/README.md).

## Promise

The tests in `tests/CheatEngine.SDK.Engine.Tests` drive a simulated Cheat Engine object model on a real Lua 5.3 state.

1. `Owned<T>` destroys its object once, never retries a destroy that raised, never destroys in another Lua runtime
   identity (consumed without a call after a re-enable or a controlled state replacement), keeps its origin across a
   transfer, and only throws from `Dispose` when the protected destroy call cannot begin; `ReleaseWithOutcome` never
   throws. Every durable resource exposes its `Origin`, and no public getter returns an owner (`OwnedTests`,
   `EngineResourceOriginTests`, `OwnershipSurfaceTests`).
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
13. Allocation ownership is consumed once; `TryAllocate` never yields a live address without an owner or a reported
    compensation, a release after detach reports `NotInvoked` and after re-enable is refused, and a released region never
    frees a later allocation at the same address (`AllocatedRegionTests`, `TargetMemoryAllocatorTests`,
    `AllocationLifecycleTests`, `TargetBoundAllocationTests`). AOB lists are owned deterministically, and the
    MemScan/FoundList state machine destroys its child before its parent (`AobScannerTests`, `MemoryScanSessionTests`).
14. Address-list and memory-record wrappers remain borrowed and intentionally do not assert an unproven main-thread
    contract; ID-addressed mutations validate hierarchy and preserve indeterminate host effects; activation reports the
    before and after state, a host refusal, a pending asynchronous activation or an indeterminate effect and never
    retries; mutations are refused during a table load (`AddressListValueTests`, `AddressListLuaTests`,
    `AddressListMutationsTests`, `MemoryRecordActivationTests`, `AddressListExitTests`).
15. Runtime metadata preserves unknown fields; an SDK-produced `RuntimeInfo` reports CE's configured pointer size and
    lists only probed capabilities; runtime probes call only read-only globals; target and host address spaces cannot be
    mixed; expected memory failures do not become exceptions (`RuntimeContractsTests`, `RuntimeObservationsTests`,
    `MemoryApiTests`).
16. Module, section, symbol and region calls distinguish documented `nil` from Lua/binding/malformed-result failures
    and never publish a partial copied destination (`EngineInspectionTests`).
17. Allocation, AOB, StringList, scan-session and address-list tests exercise ownership transfer, zero-based access,
    deterministic child-before-parent cleanup, forbidden scan state transitions, symbol leases that never unregister a
    replaced or removed name, and symbol lists unregistered before they are destroyed (`AllocatedRegionTests`,
    `AobScannerTests`, `StringListTests`, `MemoryScanSessionTests`, `AddressListLuaTests`, `AddressListMutationsTests`,
    `SymbolRegistryTests`, `SymbolLeaseReplacementTests`, `SymbolListTests`). These are fixture contracts, not a
    substitute for a controlled CE 7.7 live run.
18. Instruction profiles map CE's x86 family plus 64-bit flag to x64 and refuse contradictory, absent, no-target and
    file-as-process facts; the assembler echoes the origin, preference and range-check option it sent; target selection
    refuses local incarnation evidence for CEServer, unknown-backend and file-as-process targets
    (`InstructionOperationsTests`, `InstructionAssemblerTests`, `RuntimeProcessOperationsTests`,
    `RuntimeObservationsTests`, `TargetSelectionTests`, `RuntimeCapabilityProbesTests`). These are C1/C2 fixture
    contracts, not host qualification.
19. Auto Assembler activation reports a factual outcome with its effect state, keeps every result CE returns (disable
    information, warnings), copies host text only on request and bounded, never derives a category from it, publishes a
    bounded disable-info snapshot that never fails the activation, and `TryCheck` never creates an owner
    (`AutoAssemblerOutcomeTests`, `AutoAssemblerDisableInfoSnapshotTests`, `AutoAssemblerPatcherTests`).
20. `CheatTableFiles.TryLoad` passes exactly the path and the merge flag, and its load scope refuses re-entrant
    address-list mutations and ends on every exit path (`CheatTableFilesTests`).
21. No shipping library declares a finalizer or binds the global that deletes every registered symbol
    (`OwnershipPolicyTests` in `tests/CheatEngine.SDK.Repository.Tests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Engine.Tests -c Debug --fail-skips on
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
the [test project README](../../tests/CheatEngine.SDK.Engine.Tests/README.md).
