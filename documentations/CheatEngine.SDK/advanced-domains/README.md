# Advanced-domain boundaries

Debugger, DBVM, structure, injection and process recipes are examples, not universal host guarantees. They require an
authorized disposable target and should be verified on the exact Cheat Engine build and target process they modify.

The SDK does not infer a main-thread rule, ownership transfer or safe retry semantics from an undocumented CE global.
Recipes retain recovery data until a restore operation confirms success, and callers must not free target-owned inputs
while a timed operation can still execute. See the [capability matrix](../capability-matrix.md) for the shared evidence
boundary.

SDK-020 partitions the optional surface into individually unavailable families, each with its own owner, evidence gap,
adoption decision, prerequisites, failure boundary, and fixture/live gate. See [the advanced capability partition](capability-partitions.md).
