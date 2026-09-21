# Cheat Engine extension-surface catalogue

## Context

This directory is the machine-readable inventory for the public extension surface considered by `CheatEngine.SDK`. It records the historical Cheat Engine declarations pinned at commit `ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`, the SDK mappings that exist today, and the host/target profiles that have—or deliberately have not—been qualified.

It is not a declaration that Cheat Engine 7.7 has been executed. No profile in this initial revision reports a successful live host run.

## Why this exists

Classic Cheat Engine headers, Pascal declarations, host implementation details, the managed bootstrap, and Lua bindings are related but not interchangeable contracts. Some historical C and Pascal declarations conflict in return widths, pointer widths, or calling conventions. A source reader must not turn those disagreements into a C# cast and accidentally publish an ABI promise.

The catalogue makes that distinction reviewable:

- `ce-7.7.0.10621-x64.declarations.json` contains the exact 159-slot historical `ExportedFunctions` table, with a canonical slot-manifest hash and a source locator for every entry.
- `ce-7.7.0.10621-x64.capabilities.json` describes classic callback families, managed hosting, Lua, object ownership, target/host memory, scan, allocation, and patch surfaces. Every entry includes interop shape, affinity evidence, ownership, failure shape, availability, qualification, and profile references.
- `ce-7.7.0.10621-x64.conflicts.json` makes known historical conflicts declarative. An unresolved conflict blocks public callability and live qualification.
- `ce-7.7.0.10621-x64.host-profiles.json` keeps host facts and target facts separate. A host x64 observation never infers the target architecture or pointer width.
- `ce-7.7.0.10621-x64.advanced-families.json` is the SDK-020 deferred ledger for independently gated advanced research. Its families remain unavailable and not-qualified; it records the distinct contract, artifact, host, live, policy, and lifecycle gates required for any later adoption decision.

The `ce-7.7-classic-header-fixture-x64` profile is deliberately narrower than a CE host profile. CI compiles the local
C++ transcription with MSVC x64 and validates its versioned layout, alignment, export, and synthetic topology facts.
It can support only `fixture-only` evidence. It cannot resolve a C/Pascal conflict, prove that a real host slot is
non-null, or turn a classic ABI projection into a callable public API.

## How it improves the SDK

`eng/Validate-CeSurfaceCatalog.py` validates the catalogue documents in CI. It rejects a missing or reordered classic slot, incomplete callback coverage, duplicate capability identifiers, invalid source locators, an unsupported availability state, a source-only capability reported as live-qualified, a target-dependent capability that infers target facts, an opaque unresolved conflict exposed as callable, and an advanced family that loses a support axis, live gate, or its unavailable/not-qualified disposition.

The validator is intentionally offline: review and CI use only committed provenance. Regenerate or update a source index only after deliberately reviewing a new pinned Cheat Engine revision and its checksums. The historical source itself is not copied into this repository.

## Controlled live qualification

`tests/CheatEngine.SDK.LiveProbe` provides `ce77_live_probe_host_profile()` for an explicitly authorized, disposable target. The command records host, Lua, bridge, plugin, and target identities in a JSON result while keeping the artifact operator-retained. A result becomes evidence only after its exact artifact is reviewed; the command, its source code, or the pinned fixture does not qualify a profile by itself.

The live probe is opt-in and outside ordinary CI. It does not make the SDK, nor a CE plugin, a Native AOT unloadable plugin host. Native AOT library compatibility remains a separate build-only profile.

## Target-memory boundary

`engine.target.memory-primitives` is fixture-qualified only. The SDK maps CE's documented scalar, pointer, byte-table,
and text globals behind protected calls, but its tests use a pinned Lua stand-in rather than a live target. The entry
therefore records the explicit target-width and caller-buffer rules without claiming measured CE transfer, target
architecture, process identity, or thread affinity. A future controlled live capture must record the host and each x86
or x64 target separately before changing that qualification.
