# CESDK1020: PointerSize built from the plugin process width

|                    |                                                    |
|--------------------|----------------------------------------------------|
| Category           | `CheatEngine.SDK.Usage`                            |
| Default severity   | Warning                                            |
| Enabled by default | Yes                                                |
| Code fix           | None                                               |
| Reported           | While typing and in build                          |

In short: the width of your plugin process is not the width of the Cheat Engine target. Ask Cheat Engine:
`RuntimeProcessOperations.ObserveTargetArchitecture` returns the target bitness and Cheat Engine's configured pointer
size.

## Cause

A `CheatEngine.SDK.Engine.Runtime.PointerSize` is built from the width of the process the plugin runs in, in one of two
direct shapes:

- `new PointerSize(x)` where `x`, after implicit or explicit conversions, is `IntPtr.Size`, `UIntPtr.Size`, `nint.Size`,
  `nuint.Size`, `sizeof(nint)`, `sizeof(nuint)`, `sizeof(IntPtr)`, `sizeof(UIntPtr)`, `sizeof(void*)` (any pointer type),
  `Unsafe.SizeOf<nint>()` or `Marshal.SizeOf<IntPtr>()` (and their unsigned and pointer forms);
- a conditional expression `condition ? PointerSize.Bit64 : PointerSize.Bit32` (either order) whose condition reads one
  of those values or `Environment.Is64BitProcess`.

## Why

A Cheat Engine plugin always runs inside the 64-bit Cheat Engine process, so every one of those values is 8 (or `true`)
whatever the target is. The target is a separate process: an x86 target has 4-byte pointers, and Cheat Engine keeps
its configured pointer size (`getPointerSize`) as yet another fact that `setPointerSize` can change. A pointer width
taken from the plugin process silently reads or writes the wrong number of bytes on an x86 target (audit A07-03, F08).

## The exact definition

The rule runs only in a project that references the `CheatEngine.SDK.Engine` assembly, and it matches `PointerSize` by
metadata name and by that defining assembly, so a project-local type with the same name is ignored. It reports the two
shapes above and nothing else:

| Code                                                                        | Verdict  |
|-----------------------------------------------------------------------------|----------|
| `new PointerSize(IntPtr.Size)`, `new PointerSize((int) nuint.Size)`         | reported |
| `new PointerSize(sizeof(void*))`, `new PointerSize(Unsafe.SizeOf<nint>())`  | reported |
| `Environment.Is64BitProcess ? PointerSize.Bit64 : PointerSize.Bit32`        | reported |
| `IntPtr.Size == 8 ? PointerSize.Bit64 : PointerSize.Bit32`                  | reported |
| `new PointerSize(4)`, `PointerSize.Bit64`                                   | silent   |
| `int width = IntPtr.Size; new PointerSize(width)`                           | silent: no dataflow |
| `IntPtr.Size` used to format a host address or size a host buffer         | silent   |
| `observation.Bitness`, `observation.ConfiguredPointerSize`                  | silent   |

The rule does not follow values through locals, fields or method calls; it catches the direct shapes that make the
mistake easy to write. Generated code is not analyzed.

## Fix

Read the fact you need from Cheat Engine:

```csharp
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;

ProcessOperationStatus status = RuntimeProcessOperations.ObserveTargetArchitecture(
    out TargetArchitectureObservation target);
if (status.IsSuccess)
{
    PointerSize bitness = target.Bitness;                  // what CE's readPointer follows
    PointerSize configured = target.ConfiguredPointerSize; // CE's getPointerSize, unknown unless 4 or 8
}
```

`CurrentProcessObservation.PointerSize` (from `RuntimeProcessOperations.ObserveCurrent`) is also the target bitness.

## When to suppress

When the value really describes the plugin or the Cheat Engine host process rather than the target, for example a host
buffer that is passed to a native Cheat Engine export. Prefer `HostAddress` and host-side types for such values; a
`PointerSize` in the SDK always describes a target.
