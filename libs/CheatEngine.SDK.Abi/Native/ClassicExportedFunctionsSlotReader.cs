using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Observes one pointer-sized slot of a classic <c>ExportedFunctions</c> table from caller-bounded bytes, after
///     checking both the table's declared size and the physical buffer against that slot's minimum size.
/// </summary>
/// <remarks>
///     <para>
///         <b>Observation only.</b> The reader never dereferences, invokes, assigns or caches a slot. It exists so that
///         the rule of audit annex 04 is executable before any classic facade: reading slot <c>N</c> needs a declared
///         size of at least <c>8 * (N + 1)</c> bytes (the registry's <c>minDeclaredSize</c>), a larger table is
///         compatible, and a slot beyond the declared or physical size is refused before any byte of it is read.
///     </para>
///     <para>
///         <b>Scope.</b> Slots 1 to 158 of <c>TExportedFunctions5</c> (plugin contract 6, 159 fields, 1272 bytes on x64,
///         <c>tests/CheatEngine.SDK.Repository.Tests/Abi/TestData/classic-slot-registry.json</c>); slot 0 is the declared
///         size itself. No managed-hostfxr plugin
///         receives this table, so there is no production caller: the classic facade is deferred and every slot stays
///         <c>NotObserved</c> on the qualifiable profile. x64 only, like the rest of the assembly.
///     </para>
/// </remarks>
[SuppressMessage("Meziantou.Analyzer", "MA0182",
	Justification =
		"Intentionally internal, observation-only classic slot reader, exercised by friend-assembly Q39 tests; no production caller exists because the classic facade is deferred (no managed-hostfxr route reaches the classic table).")]
internal static class ClassicExportedFunctionsSlotReader
{
	/// <summary>Number of fields of the host table type <c>TExportedFunctions5</c>, slot 0 included.</summary>
	internal const int SlotCount = 159;

	/// <summary>x64 size of the complete table: 8 bytes per slot, the 32-bit slot 0 padded to 8.</summary>
	internal const int TableByteCount = 1272;

	private const int SlotByteCount = 8;

	/// <summary>
	///     Reads the raw value of <paramref name="slot" /> when the declared size and the physical buffer both reach
	///     <c>8 * (slot + 1)</c> bytes.
	/// </summary>
	/// <param name="tableBytes">The table bytes, starting at its 32-bit declared-size field.</param>
	/// <param name="slot">A slot index from 1 to 158.</param>
	/// <param name="observation">The observation, or <see langword="default" /> when refused.</param>
	/// <returns>
	///     <see langword="false" /> for an out-of-range slot, a non-x64 process, a buffer shorter than the declared-size
	///     field, or a declared or physical size below the slot's minimum; otherwise <see langword="true" />.
	/// </returns>
	internal static bool TryReadSlot(ReadOnlySpan<byte> tableBytes, int slot, out ClassicSlotObservation observation)
	{
		observation = default;
		if (slot < 1 || slot >= SlotCount || !AbiArchitecture.IsSupported || IntPtr.Size != SlotByteCount)
		{
			return false;
		}

		if (tableBytes.Length < sizeof(int))
		{
			return false;
		}

		int requiredBytes = SlotByteCount * (slot + 1);
		int declaredSize = MemoryMarshal.Read<int>(tableBytes);
		if (declaredSize < requiredBytes || tableBytes.Length < requiredBytes)
		{
			return false;
		}

		nint rawValue = MemoryMarshal.Read<nint>(tableBytes.Slice(SlotByteCount * slot, SlotByteCount));
		observation = new ClassicSlotObservation(slot, rawValue);
		return true;
	}
}
