using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>A copied observation of Cheat Engine's currently selected target process.</summary>
/// <remarks>
///     <para>
///         This observation carries the selected process identifier and the process bitness only. Its
///         <see cref="PointerSize" /> is Cheat Engine's 64-bit process flag (<c>targetIs64Bit</c>) as a width, which is
///         what CE's <c>readPointer</c> follows (spike C3 D3d); it is not Cheat Engine's configured pointer size
///         (<c>getPointerSize</c>), which <c>setPointerSize</c> can change independently. Read the configured size with
///         <see cref="RuntimeProcessOperations.TryGetConfiguredPointerSize" /> and the separate ISA-family, ABI, Android
///         and backend facts with <see cref="RuntimeProcessOperations.ObserveTargetArchitecture" />.
///     </para>
///     <para>
///         A 32- or 64-bit width alone cannot distinguish x86 from ARM, so this type states no target architecture.
///         Higher layers may combine it with independently acquired local metadata, but must not treat that metadata
///         as atomically coupled to the selection.
///     </para>
/// </remarks>
/// <param name="Id">The positive selected process identifier from <c>getOpenedProcessID</c>.</param>
/// <param name="PointerSize">The process bitness from <c>targetIs64Bit</c>; not the configured pointer size.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct CurrentProcessObservation(TargetProcessId Id, PointerSize PointerSize);
