# CheatEngine.SDK source inventory

The SDK's CE 7.7 fixture baseline is `7.7.0.10621` x64. Each library README identifies the exact source and hash where
an ABI layout or host behavior depends on installed Cheat Engine material. The repository keeps the Lua test fixture in
[`native/cheat-engine`](../../native/cheat-engine/README.md) and records public API contracts beside their owning
libraries.

Use this index with the [capability matrix](capability-matrix.md): source text establishes only what it actually says;
ownership, thread affinity and runtime behavior remain unknown until a controlled live result proves them. Per-symbol
locators, pinned revisions, checksums, and qualification are maintained in the [extension-surface catalogue](catalog/README.md).
