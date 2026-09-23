# CESDK7001: PointerSize.FromArchitecture is obsolete

|                    |                                                                              |
|--------------------|------------------------------------------------------------------------------|
| Kind               | `[Obsolete]` diagnostic of the C# compiler, raised with this identifier      |
| Obsoleted API      | `CheatEngine.SDK.Engine.Runtime.PointerSize.FromArchitecture`                |
| Default severity   | Warning (an error under `TreatWarningsAsErrors`)                             |
| Obsoleted in       | CheatEngine.SDK 2.0                                                          |
| Removal            | Not before the next major version after 2.0; the method body is unchanged    |
| Replacement        | `TargetArchitectureObservation.ConfiguredPointerSize` or `.Bitness`          |

In short: do not turn an architecture into a pointer size. Ask Cheat Engine for the fact you need:
`RuntimeProcessOperations.ObserveTargetArchitecture` returns the target bitness and CE's configured pointer size as two
separate facts.

## Cause

Code calls `PointerSize.FromArchitecture(CheatEngineArchitecture)`.

## Why

`FromArchitecture` returns the natural width of an instruction set: 4 bytes for x86 and ARM32, 8 bytes for x64 and
ARM64. Its name suggests more than that, and callers used it as the width of Cheat Engine's pointers. Cheat Engine keeps
three facts that this method silently merges:

| Fact                           | Cheat Engine source | SDK member                                                   |
|--------------------------------|---------------------|--------------------------------------------------------------|
| Target ISA family              | `targetIsX86`, `targetIsArm` | `TargetArchitectureObservation.IsX86Family`, `IsArmFamily`, `Architecture` |
| Target bitness                 | `targetIs64Bit`     | `TargetArchitectureObservation.Bitness`                      |
| CE's configured pointer size   | `getPointerSize`    | `TargetArchitectureObservation.ConfiguredPointerSize`, `RuntimeInfo.PointerSize` |

The configured pointer size is per-attachment state that `setPointerSize` sets to any integer. On Cheat Engine
7.7.0.10621 x64 with an x64 target, `setPointerSize(4)` made `getPointerSize()` return 4 while `targetIs64Bit()` stayed
true, and `readPointer` kept reading 8 bytes. `setPointerSize(2)` was accepted, and selecting the target again reset
the value. That observation is the spike C3 D3 design input (a Lua-only run on the pinned host, not a qualification of
SDK code). An architecture therefore determines neither the configured size nor the bitness, and the plugin's own
`IntPtr.Size` determines neither (audit F08, A07-03, A07-04).

## The exact definition

The compiler reports `CESDK7001` for every reference to `PointerSize.FromArchitecture` outside an
`#pragma warning disable CESDK7001` region, because the method carries
`[Obsolete(..., DiagnosticId = "CESDK7001")]`. The ordinary obsoletion warning `CS0618` is not reported for it, so a
`CS0618` suppression does not silence this one.

## Fix

Read the fact you need from Cheat Engine instead of deriving it:

```csharp
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;

ProcessOperationStatus status = RuntimeProcessOperations.ObserveTargetArchitecture(
    out TargetArchitectureObservation target);
if (status.IsSuccess)
{
    PointerSize bitness = target.Bitness;                 // what CE's readPointer follows
    PointerSize configured = target.ConfiguredPointerSize; // CE's getPointerSize, unknown unless 4 or 8
}
```

`RuntimeProcessOperations.TryGetConfiguredPointerSize` reads only the configured size and keeps any raw integer.
`RuntimeObservations.TryObserveRuntimeInfo` produces a `RuntimeInfo` whose `PointerSize` is the configured size and
whose `Target.Bitness` is the bitness.

## When to suppress

Only in code that needs the natural instruction width of an architecture value it already holds, for example a test
that pins the 1.0.0 behaviour. Suppress it locally with `#pragma warning disable CESDK7001` and a comment saying why.
