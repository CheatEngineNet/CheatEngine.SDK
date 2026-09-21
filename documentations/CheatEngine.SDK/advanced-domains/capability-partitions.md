# Independently gated advanced capability families

This is the SDK-020 research partition. It is a ledger and qualification plan, not an API catalogue, feature flag, or support promise. Every row remains **unavailable** and **not qualified**. A source locator, Lua global, classic slot, existing interface, managed fixture, or explicit policy opt-in cannot change that state.

The machine-readable authority is [`ce-7.7.0.10621-x64.advanced-families.json`](../catalog/ce-7.7.0.10621-x64.advanced-families.json). The existing source-indexed surface catalogue remains authoritative for already-recorded individual SDK mappings and conflicts.

| Family | Narrow adoption boundary | Separate live evidence required before an implementation issue may be opened |
|---|---|---|
| UI/forms | SDK-created root form lifecycle, then controls/events, with CE-owned UI excluded. | Slot presence, dispatch/reentrancy, callback replacement, close/free and child cleanup. |
| Debugger | State observation, breakpoint lifecycle, and synchronous continuation are distinct. | Backend selection, native callback ABI/lifetime, removal, disable/re-enable, target replacement. |
| Mono | Collector bootstrap, runtime attachment, metadata and callbacks are separate. | Collector/path/protocol/export identities, pipe/thread cleanup, runtime-specific target lifecycle. |
| IL2CPP | Discovery and symbol materialization are distinct from Mono. | IL2CPP export set, cancellation/UI behavior, SymbolList cleanup and target replacement. |
| DBVM | Admission, watches, timing, physical/MSR operations remain independent. | Isolated recoverable machine, driver/VM state, exact operation/removal or reset outcome. |
| Speedhack | Local injection, CE-server route, and callback route remain independent. | Route identity, timing baseline/restore, hook residual state, target lifecycle. |
| Hotkeys | Generic host input registration only; record-owned hotkeys are excluded. | Chord semantics, callback thread/reentrancy, unregister and no late callback. |
| Timers | Component/timer lifetime is separate from form lifecycle. | Interval/callback behavior, destruction order, stale-tick exclusion. |
| Structures | Registry/definition, inference, and target writes remain independent. | Handle/list ownership, update balancing, inference accuracy, separate write/restore proof. |
| Auto Assembler | Validation, reversible patches, and extension callbacks remain independent. | Retained disable-info, exact target identity, partial-effect result, callback ABI and restoration. |
| Remote execution/injection | Module injection and target execution are separately authorized. | Target architecture/incarnation, timeout/unknown-effect handling, allocation/module cleanup. |
| Hashing | Bounded target-memory and local-file hashes are distinct modes. | Approved read contract, resource release, path trust, and stale-target exclusion. |

## Common boundary

SDK owns CE mappings, native safety, factual outcomes, and low-level resource owners. Client can only compose a later approved SDK contract into policy and workflow; it must not recreate collector, ABI, Lua, callback, target, or cleanup mechanics. Every target effect remains limited to an operator-authorized, local, disposable process. DBVM additionally requires a separately authorized recoverable machine.

Each family carries six independent axes: approved SDK contract, containing artifact identity, exact-host observation, reviewed live qualification, explicit policy opt-in, and lifecycle/cleanup proof. All must be independently satisfied for that family; a passed family, source fixture, or policy switch never supplies another family's evidence.

## Qualification protocol

The ordinary fixture validates only deterministic mapping and failure behavior. A family-specific live gate must use the exact CE host and a disposable authorized fixture, capture host/Lua/bridge/plugin/target identities, exercise success and expected failure, clean up, disable, re-enable, and replace the target where relevant. Raw captures remain operator-retained until reviewed. An incomplete, denied, hung, or cleanup-uncertain run is evidence of the limitation and leaves the family unavailable.

For a future implementation issue, first approve one concrete public contract and name its containing SDK artifact. Its corresponding ledger row then needs a new reviewed source/fixture/live record; it must not be promoted by editing availability because a name or interface exists.
