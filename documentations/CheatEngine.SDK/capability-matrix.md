# CheatEngine.SDK capability matrix

This matrix records what the SDK can state about Cheat Engine 7.7.0.10621 x64. It is deliberately an evidence index,
not a substitute for a live-host result. Fixture tests establish managed behavior against the bundled Lua 5.3 fixture;
an item is marked live only after the controlled procedure in [live probes](live-probes/README.md) records it.

| Area | Current contract | Evidence boundary |
|---|---|---|
| Plugin lifecycle and Lua attachment | Hosting attaches Lua for `OnEnable` and `OnDisable`, then neutralizes callbacks during teardown. | Managed lifecycle tests; live dispatch remains separately qualified. |
| Target and host addresses | `Address` and `HostAddress` are distinct API types. | Managed type and memory API tests. |
| Engine object ownership | Only factories with a documented caller-owned result create `Owned<T>`. GUI-owned objects stay borrowed. | CE 7.7 source catalogue plus managed ownership tests. |
| Memory, inspection and scans | Results preserve unavailable, Lua-error and malformed-result distinctions where documented by the API. | Pinned Lua fixture and managed tests; CE timing and affinity are not inferred. |
| Advanced domains | Debugger, DBVM, structures and process interactions retain their stated per-recipe limitations. | See [advanced-domain boundaries](advanced-domains/README.md). |

The source records supporting these entries are indexed in [SOURCES.md](SOURCES.md). Update this file together with an
API contract when new CE source or live evidence changes the boundary. The source-indexed, machine-validated
[extension-surface catalogue](catalog/README.md) is authoritative for per-symbol availability and qualification.
