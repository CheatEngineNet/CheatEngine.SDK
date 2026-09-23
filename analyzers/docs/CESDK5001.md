# CESDK5001: Worker-thread Lua admission is experimental

|                    |                                                                                 |
|--------------------|---------------------------------------------------------------------------------|
| Kind               | `[Experimental]` API gate (reported by the C# compiler, not by an SDK analyzer) |
| Default severity   | Error (compiler)                                                                |
| Enabled by default | Yes                                                                             |
| Code fix           | None                                                                            |
| Reported           | While typing and in build, at every use of a gated member                       |

In short: opting a worker thread into Lua admission is unqualified. Handle every `LuaAdmissionStatus` your code can
now receive, and do not assume the shared Lua heap is safe to touch from two threads at once.

## Cause

Your code calls `CheatEngine.SDK.Lua.Runtime.LuaRuntime.AdmitWorkerThreads()`, which carries
`[Experimental("CESDK5001")]`.

## Why it is experimental

The SDK 2.0 default (`LuaThreadAdmission.MainThreadOnly`, ADR-07, F04) starts no Lua work on a thread the host is not
already running Lua on: a worker's `LuaRuntime.AcquireOperation()`-family call is refused with
`LuaAdmissionStatus.ThreadNotAdmitted` before the host's state provider ever runs. This is the conservative default
because Cheat Engine hands out one Lua thread (coroutine) per OS thread from one shared virtual machine, heap and
registry — admission only makes this SDK copy's own attach/reset/detach transitions exclusive, never the shared heap.
Whether it is actually safe to run Lua concurrently from a first-admitted worker thread, on the pinned Cheat Engine
host, is exactly what qualification scenario Q19 measures.

`AdmitWorkerThreads()` lifts the admission refusal only; it does not by itself prove, or provide, any serialization of
the shared Lua heap across threads. It stays gated until Q19 passes at **both**:

- **C3** (exact host, one plugin, first worker call observed and measured), and
- **C4** (exact host, two SDK copies, simultaneous first worker calls observed and measured).

No local Cheat Engine qualification run is currently available for either level, so both remain open. The
`[Experimental]` gate is the honest reflection of that: the SDK is telling you it has not seen this behaviour on the
real host yet.

## How to opt in

Suppress the diagnostic where you accept the gate, as narrowly as possible, and call it again from every `OnEnable`
(every `Attach`/`Detach` reverts to the conservative default):

```csharp
#pragma warning disable CESDK5001 // Accepted: single-threaded worker pool, no concurrent Lua calls across threads.
CheatEngine.SDK.Lua.Runtime.LuaRuntime.AdmitWorkerThreads();
#pragma warning restore CESDK5001
```

or for a whole project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CESDK5001</NoWarn>
</PropertyGroup>
```

Never suppress it project-wide from an entry point that is not itself calling `AdmitWorkerThreads()`; keep the
suppression next to the call site so a reviewer sees exactly what accepted the gate.

## What is still unqualified

- Q19 at C4 (two SDK copies, both opted in, simultaneous first worker calls).
- The worker-side `synchronize` hand-off's heap safety under real concurrent Lua traffic (its admission itself is not
  gated: it is the SDK's single documented default exception, not this opt-in).
- Multi-plugin concurrency in general (see the Hosting README, "Plugin identity and coexistence").
- Whether two worker threads racing `AdmitWorkerThreads()`-opted-in calls at the same time observe a consistent Lua
  stack: `LuaThreadAdmissionTests` proves only that both are refused by the *default*, not that both succeed safely
  once opted in.

## When the gate is removed

A Q19 C3 receipt (exact host, one plugin) **and** a Q19 C4 receipt (exact host, two SDK copies) that both record a
successful, observed first worker Lua call retire this identifier; it is never reused.
