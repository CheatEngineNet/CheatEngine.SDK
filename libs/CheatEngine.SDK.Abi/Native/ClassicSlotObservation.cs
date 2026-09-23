using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     One raw read of one slot of a classic <c>ExportedFunctions</c> table, as returned by
///     <see cref="ClassicExportedFunctionsSlotReader.TryReadSlot" />.
/// </summary>
/// <remarks>
///     <para>
///         An observation, not a capability: <see cref="RawValue" /> is the pointer-sized value the table held when it
///         was read. Depending on the slot (see <c>tests/CheatEngine.SDK.Repository.Tests/Abi/TestData/classic-slot-registry.json</c>) it is a function address,
///         the address of a host cell that holds a function pointer, the address of a host variable, or nil. Nothing
///         here dereferences, invokes or caches it; two reads of the same slot are two independent observations.
///     </para>
///     <para><b>Layout (64-bit): 16 bytes.</b> <see cref="Slot" /> 0, <see cref="RawValue" /> 8.</para>
/// </remarks>
[SuppressMessage("Meziantou.Analyzer", "MA0182",
	Justification =
		"Internal observation value of the classic slot reader, exercised by friend-assembly Q39 tests. The classic facade that would consume it is deferred (no managed-hostfxr route reaches the classic table).")]
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ClassicSlotObservation
{
	/// <summary>Initializes an observation of <paramref name="slot" />.</summary>
	/// <param name="slot">The slot index, 1 to 158.</param>
	/// <param name="rawValue">The pointer-sized value read at offset <c>8 * slot</c>.</param>
	internal ClassicSlotObservation(int slot, nint rawValue)
	{
		Slot = slot;
		RawValue = rawValue;
	}

	/// <summary>The slot index (offset 0).</summary>
	internal readonly int Slot;

	/// <summary>The raw pointer-sized value of the slot (offset 8). Never dereferenced by the SDK.</summary>
	internal readonly nint RawValue;

	/// <summary>Gets a value indicating whether the slot held nil: nothing to call, observe or hook.</summary>
	internal bool IsNull => RawValue == 0;
}
