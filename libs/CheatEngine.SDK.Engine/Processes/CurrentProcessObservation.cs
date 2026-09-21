using System.Runtime.InteropServices;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>A copied observation of Cheat Engine's currently selected target process.</summary>
/// <remarks>
///     Cheat Engine's process globals establish only the selected process identifier and pointer width. They do not
///     establish a target ISA, process name, executable path, handle, or process-lifetime guarantee. A 32- or 64-bit
///     pointer width alone cannot distinguish x86 from ARM, so the target architecture remains unknown. Higher layers
///     may combine this value with independently acquired local metadata, but must not treat that metadata as atomically
///     coupled to the selection.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct CurrentProcessObservation(TargetProcessId Id, PointerSize PointerSize);
