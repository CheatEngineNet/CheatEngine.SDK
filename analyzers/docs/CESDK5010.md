# CESDK5010: Scan deadline and cooperative termination are experimental

|                    |                                                                                 |
|--------------------|---------------------------------------------------------------------------------|
| Kind               | `[Experimental]` API gate (reported by the C# compiler, not by an SDK analyzer) |
| Default severity   | Error (compiler)                                                                |
| Enabled by default | Yes                                                                             |
| Code fix           | None                                                                            |
| Reported           | While typing and in build, at every use of a gated member                       |

In short: these members call two Cheat Engine 7.7 behaviours that have not yet been observed on the pinned host. Opt in
only if you accept that, and handle every status they can return.

## Cause

Your code calls one of these `CheatEngine.SDK.Engine` members, which carry
`[Experimental("CESDK5010")]`:

| Member                                                           | Relies on                                                         |
|------------------------------------------------------------------|-------------------------------------------------------------------|
| `MemoryScanSession.TryWaitForCompletion(TimeSpan)`               | the `false` (timed-out) result of `MemScan.waitTillDone(timeout)` |
| `MemoryScanSession.TryTerminateScan(TimeSpan)`                   | `MemScan.terminateScan(false)` and the bounded wait that follows  |
| `AobScanner.TryScanWithinBounds(..., TimeSpan waitTimeout, ...)` | both, when the call deadline expires before the scan completes    |

## Why it is experimental

Cheat Engine 7.7.0.10621 documents an optional timeout for `MemScan.waitTillDone` that returns a boolean (`celua.txt`
line 2649) and a cooperative `MemScan.terminateScan` (line 2566). The C3 spike of 2026-09-22 (Lua-only, on the pinned
`ce-7.7.0.10621-x64-managed-hostfxr` profile, decision D4.7) observed `waitTillDone(timeout)` returning `true`, but
could
not produce the `false` path: a whole-address-space scan finished before a one-millisecond deadline expired. It did not
exercise `terminateScan` at all. Everything these members do after a deadline expires is therefore covered by fixture
tests (C1) only, not by the host.

What the SDK does guarantee, and tests at C1: it never forces termination (a forced stop can kill CE's scan thread and
open a modal dialog on CE's main thread), requests the cooperative stop at most once, never retries it, and reports an
unconfirmed stop instead of claiming one. CE's waits can run queued main-thread work that calls back into the session;
while one of these members is inside a CE call, the session refuses its other members and defers a release until that
call has returned, so it never destroys the scanner under its own wait. How long CE's stop and destroy really block, and
whether they behave as the fixture assumes, is exactly what the host has not shown yet.

The bounded scan without a deadline,
`AobScanner.TryScanWithinBounds(string, AobScanBounds, AobScanOptions, Span<Address>, CancellationToken)`, is not
gated: the spike settled its range semantics, completeness and cost.

## How to opt in

Suppress the diagnostic where you accept the gate, as narrowly as possible:

```csharp
#pragma warning disable CESDK5010 // Deadline-bounded scan: every MemoryScanWaitStatus is handled below.
MemoryScanWaitStatus status = session.TryWaitForCompletion(TimeSpan.FromSeconds(2));
#pragma warning restore CESDK5010
```

or for a whole project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CESDK5010</NoWarn>
</PropertyGroup>
```

Handle `MemoryScanWaitStatus.TimedOut` (the scan may still be running: wait again, stop it, or release the session) and
every `MemoryScanTerminationStatus` other than `Confirmed` (the stop is unconfirmed: the session cannot be reset, only
released or abandoned).

## When the gate is removed

A Q29 C3 receipt on the pinned profile that records a deadline returning `TimedOut` and a termination returning
`Confirmed` retires this identifier; it is never reused.
