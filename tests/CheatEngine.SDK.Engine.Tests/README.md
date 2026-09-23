# CheatEngine.SDK.Engine.Tests

Tests for `CheatEngine.SDK.Engine`: object handles, ownership, value conventions and enums, driven by a fake Cheat
Engine object model on a real Lua 5.3 library.

## Objective

Prove the managed contracts of `CEObject`, explicit owners, target/host addresses, runtime capabilities, memory,
inspection, allocation, AOB/StringList, scans and address-list handles without a running Cheat Engine host.

## Why it exists

Engine code runs inside Cheat Engine against an object model the SDK cannot inspect. The userdata decoder
`CEObject.TryRead`, the access primitives and their allocation budget must be provable off-host.

## How it works

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. The other tests need no
native code. See [
`tests/CheatEngine.SDK.Tests.Shared/README.md`](../CheatEngine.SDK.Tests.Shared/README.md).

| Piece                         | Role                                                                                                                                                                                                                           |
|-------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Support/FakeHost.cs`         | Stands in for `GetLuaState` and `LuaPushClassInstance`, not for Lua. A Lua model supplies object, list, scanner and address-list stand-ins, getters and setters that can raise, zero-based `obj[i]` and `destroy` bookkeeping. |
| `Support/FakeHost.SRes.cs`    | Adds the S-RES fixtures: a controlled state replacement (`ReplaceStateGeneration`), a symbol-list class, a qualified local target, and a re-entrant host hook that calls back into the SDK from inside a Lua call.             |
| `Support/HostScope.cs`        | Attaches `LuaRuntime` to a fixture state for one test and detaches on dispose. Tests attach the runtime only through it, and tests that need it unattached call `LuaRuntime.Detach()` first.                                   |
| `Support/DebugAssertScope.cs` | Turns a failed `Debug.Assert` into an exception, so the Debug-only main-thread guard of `Owned<T>` is testable. That test skips in Release.                                                                                    |
| `Support/EngineTest.cs`       | `RequireNativeLua()` skips without a Lua library. `RunOnWorker` runs work on a fresh thread and returns what it threw.                                                                                                         |
| `AssemblyInfo.cs`             | Runs tests sequentially, because `LuaRuntime` and the fake host are process-wide.                                                                                                                                              |
| `Scanning/MemScanTestHost.cs` | Lua stand-ins for `createMemScan`, `createFoundList` and the MemScan/FoundList members the SDK calls; every call is traced, and Lua globals steer each result shape (wait result, stop, error text, rows, target).             |

Each fake object is a Lua table found by its pointer, so every push of one pointer finds the same state. Pointers are
synthetic and never dereferenced. The double follows the assumed userdata layout, so the suite cannot prove that Cheat
Engine's own `LuaPushClassInstance` uses it. Likewise, the suite proves the SDK's status, state and ownership rules; it
does not turn a fixture response into live CE 7.7 proof of thread affinity, allocation ownership or undocumented Lua
behavior.

## Promise

- A raising getter, setter or method reaches the caller as a status with one error value, never as an exception. Tests
  assert `L.Top` after each primitive.
- Hot paths allocate exactly zero bytes after warm-up, measured by `Support/AllocationGate.cs`: typed get, set and call,
  stack-level primitives, `Address` and enum marshalling, enum name lookup.
- A typed operation acquires the state once and pushes the object once (`HostCallCountTests`).
- `Owned<T>` destroys the object exactly once. After the plugin is disabled, `Dispose` throws and leaves the object
  alive; after a re-enable or a controlled state replacement, every release path consumes the owner without a call and
  reports `RefusedRuntimeChanged`. `ReleaseWithOutcome` never throws, and a transfer keeps the origin
  (`OwnedTests`, `EngineResourceOriginTests`, `OwnershipSurfaceTests`).
- Enum values and Cheat Engine names are pinned by literals.
- `RuntimeInfo`/`RuntimeCapabilities` retain explicit unknown facts. `TargetMemory` and `HostMemory` keep their address
  types separate, preserve byte ordering through span calls, and distinguish expected read/write failures.
- Runtime and target facts (ISA family, bitness, configured pointer size, ABI, Android, host bitness, OS, file version,
  backend) are read separately, PID first, with the spike C3 values in `Support/FakeHost.Rt.cs`; absent globals stay
  unknown, raising and malformed probes keep distinct statuses, and the probes touch only a read-only allowlist of
  globals (`RuntimeProcessOperationsTests`, `RuntimeHostOperationsTests`, `RuntimeObservationsTests`,
  `InstructionOperationsTests`, `InstructionAssemblerTests`, `RuntimeCapabilityProbesTests`).
- `TargetSelection` emits local creation-time evidence only when `isConnectedToCEServer()` returned `false`; CEServer,
  unknown-backend and file-as-process selections are refused without a process lookup (`TargetSelectionTests`). Every
  fixture that models a qualified local target therefore defines `isConnectedToCEServer` (use
  `FakeHost.LocalTargetBackendChunk`).
- Inspection snapshots distinguish documented `nil` from malformed results. Allocation ownership is consumed exactly
  once even when the underlying release fails; `TryAllocate` reports every result with its effect state and never
  leaves a live address without an owner or one reported compensation; a released region never frees a later
  allocation at the same address (`TargetMemoryAllocatorTests`, `AllocationLifecycleTests`). AOB and StringList results
  are explicit `Owned<T>` values.
- Auto Assembler activation reports a factual outcome, copies CE text only on request and bounded, and publishes a
  bounded disable-info snapshot that never fails the activation (`AutoAssemblerOutcomeTests`,
  `AutoAssemblerDisableInfoSnapshotTests`).
- Symbol leases never unregister a replaced or removed name, and a registered symbol list is unregistered before it is
  destroyed (`SymbolLeaseReplacementTests`, `SymbolListTests`).
- A scan session enforces its state machine and destroys its owned `FoundList` before its `MemScan`, after one
  cooperative stop when a scan may still run. AOB zero matches as CE 7.7 reports them (no value) are `NoResult`; the
  bounded AOB route is exhaustive, post-filters its start and reports a factual `NoMatches`; the chapter-13 battery is
  covered at fixture level and tagged `Q25`–`Q29` (`AobScannerTests`, `AobBoundedScanTests`, `AobFirstFoundScanTests`,
  `MemoryScanSessionReleaseTests`, `MemoryScanSessionDeadlineTests`, `MemoryScanSessionBatteryTests`). Address-list and
  memory-record handles remain CE-borrowed and are never implicitly owned. Activation reports its before and after
  state and never retries, and a table load refuses re-entrant mutations (`MemoryRecordActivationTests`,
  `AddressListExitTests`).
- Address-list and memory-record wrappers also omit `MainThreadOnly` metadata until the CE 7.7 dispatcher probe turns
  their GUI affinity inference into an enforceable contract (`AddressListValueTests`).
- Memory text keeps embedded NULs and raw invalid UTF-8 in the byte forms, `maximumLength` and the wide flag reach CE
  unchanged, and a partial byte read reports its prefix (`MemoryTextFidelityTests`, qualification Q20). The generated
  `MemoryScalars` read a signed -1, pass an address above 4 GiB as an exact Lua integer and keep `nil`, `false` and a
  raise apart from 0 (`MemoryScalarsFidelityTests`, Q21 and Q22). `Address`, `HostAddress` and `CEObject` expose no
  conversion to each other or to a floating-point type (`AddressTypeSeparationTests`, Q21).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Engine.Tests -c Debug --fail-skips on
dotnet test --project tests/CheatEngine.SDK.Engine.Tests -c Debug --filter-trait "Category=NativeLua" --fail-skips on
```
