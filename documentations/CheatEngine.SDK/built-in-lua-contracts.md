# Built-in Cheat Engine Lua contracts

## Context

`CheatEngine.Client` originally declared the following host-global signatures in
[`ClientLuaGlobals.cs`](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Infrastructure/ClientLuaGlobals.cs)
at commit `923a4ded85898f53ef4cd2ff5872d2fd9001071a`.
They are integration facts, so their SDK owners live in `CheatEngine.SDK.Engine`.
This record is the correspondence boundary for SDK-008. It is source-mapped
and fixture-verified only: it does not claim that the source currently built
here is available from a published package or qualified against a live Cheat
Engine host.

## Ownership map

| Client source record | CE global | SDK owner | Result and lifetime contract |
|---|---|---|---|
| `ClientLuaGlobals.cs:10` | `getOpenedProcessID` | `Processes.RuntimeProcessOperations.ObserveCurrent` | `ProcessOperationStatus`; zero is `TargetNotAttached`; copied PID and pointer width only. |
| `ClientLuaGlobals.cs:13` | `openProcess` | `Processes.RuntimeProcessOperations.SelectAndObserve` | `ProcessOperationStatus`; normal return is verified immediately against the requested PID. |
| `ClientLuaGlobals.cs:16` | `getCEVersion` | `Processes.RuntimeHostOperations.TryGetCheatEngineVersion` | `LuaOperationStatus`; a finite legacy floating-point observation, never a synthesized file version. |
| `ClientLuaGlobals.cs:19` | `getSystemArchitecture` | `Processes.RuntimeHostOperations.TryGetSystemArchitecture` | `LuaOperationStatus`; documented discriminants only; an unknown code is `InvalidResult`. |
| `ClientLuaGlobals.cs:22` | `getABI` | `Processes.RuntimeHostOperations.TryGetTargetAbi` | `LuaOperationStatus`; documented discriminants only; an unknown code is `InvalidResult`. |
| `ClientLuaGlobals.cs:25` | `targetIs64Bit` | `Processes.RuntimeProcessOperations.ObserveCurrent` | `ProcessOperationStatus`; copied 32/64-bit pointer width; it does not infer target ISA. |
| `ClientLuaGlobals.cs:28` | `loadTable` | `Tables.CheatTableFiles.TryLoad` | `LuaOperationStatus`; opaque path and explicit merge flag; no path authorization. |
| `ClientLuaGlobals.cs:31` | `saveTable` | `Tables.CheatTableFiles.TrySave` | `LuaOperationStatus`; opaque path; no path authorization. |
| `ClientLuaGlobals.cs:34` | `getNameFromAddress` | `Inspection.SymbolRegistry.TryGetName` | `LuaOperationStatus`; copied managed string; no CE object or Lua reference escapes. |
| `ClientLuaGlobals.cs:37` | `registerSymbol` | `Inspection.SymbolRegistry.Register` / `TryRegisterOwned` | `LuaOperationStatus`, or an explicit same-SDK cleanup coordinator; CE exposes no opaque token, so this is not exclusive host-wide ownership. |
| `ClientLuaGlobals.cs:40` | `unregisterSymbol` | `Inspection.SymbolRegistry.Unregister` | `LuaOperationStatus`; explicit cleanup primitive with no name-selection policy. |

## Boundary rules

Every owner acquires a synchronous `LuaRuntime` operation, resolves the exact
global through its attach-epoch-aware cache, and restores the Lua stack before
returning. No contract above dispatches work, stores a `LuaState`, exposes a CE
object, owns the selected process, or promises atomicity against an external CE
target change. The documented source records do not establish an enforceable
main-thread rule for this set, so none is inferred.

The Client remains responsible for exact-name process policy, local process
metadata, DI, table-root authorization, trusted-table workflows, and its own
activation/resource cleanup orchestration. The SDK owns only the CE call shape,
factual outcome, copied values, and Lua lifetime boundary.

`AddressListMutations` is the matching typed command boundary for record deletion and hierarchy changes. It accepts
record IDs rather than exposing a raw host object, resolves the ID in the current `getAddressList()` result while one
Lua operation is admitted, bounds its parent walk, and reports `NotAttempted`, `Completed`, or `Indeterminate`. A
completed command is not a snapshot; consumers obtain any post-command copy separately. These are fixture-backed call
contracts, not a claim of live-table qualification or a proven GUI-thread rule.
