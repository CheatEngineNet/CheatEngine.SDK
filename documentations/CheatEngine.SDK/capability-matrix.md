# CheatEngine.SDK capability matrix

This matrix records what the SDK can state about Cheat Engine 7.7.0.10621 x64. It is deliberately an evidence index,
not a substitute for a live-host result. Fixture tests establish managed behavior against the bundled Lua 5.3 fixture;
an item is marked live only after the controlled procedure in [live probes](live-probes/README.md) records it.

| Area | Current contract | Evidence boundary |
|---|---|---|
| Classic ABI layouts | The native fixture validates the checked-in x64 C-header transcription, physical table prefix, direct-address/value-cell/null topology, and plugin exports. Conflicting, nil, and hookable slots remain opaque. | MSVC x64 fixture only; no installed header or live CE host is loaded. |
| Lua state universe, reset and worker pointers | A worker coroutine can have a distinct Lua state pointer while sharing the main virtual machine, heap and registry. The SDK identity is attachment epoch plus reset generation, not a pointer; reset closes admission and invalidates references and callbacks before replacement. | SDK-012 deterministic native fixture and protected-operation tests; live multi-threaded execution remains authorization-gated observation. |
| Generated Lua global registration | `TryRegisterLuaFunctions` publishes one explicit ownership lease. Default collision preflight is non-destructive; release uses exact Lua identity, preserves later replacements, reports partial cleanup, and does nothing against a stale epoch/generation. | SDK-009 deterministic native Lua tests and package-consumer compile; no Cheat Engine host execution. |
| Plugin lifecycle and Lua attachment | Hosting attaches Lua for `OnEnable` and `OnDisable`, then neutralizes callbacks during teardown. | Managed lifecycle tests; live dispatch remains separately qualified. |
| Target and host addresses | `Address` and `HostAddress` are distinct API types. | Managed type and memory API tests. |
| Engine object ownership | Only factories with a documented caller-owned result create `Owned<T>`. GUI-owned objects stay borrowed. | CE 7.7 source catalogue plus managed ownership tests. |
| Memory, inspection and scans | Results preserve unavailable, Lua-error and malformed-result distinctions where documented by the API. | Pinned Lua fixture and managed tests; CE timing and affinity are not inferred. |
| Advanced domains | Debugger, DBVM, structures and process interactions retain their stated per-recipe limitations. No family is available from source presence, a global name, an interface, or an opt-in. | See [advanced-domain boundaries](advanced-domains/README.md) and the [independent family partition](advanced-domains/capability-partitions.md). |

The source records supporting these entries are indexed in [SOURCES.md](SOURCES.md). Update this file together with an
API contract when new CE source or live evidence changes the boundary. The source-indexed, machine-validated
[extension-surface catalogue](catalog/README.md) is authoritative for per-symbol availability and qualification.
