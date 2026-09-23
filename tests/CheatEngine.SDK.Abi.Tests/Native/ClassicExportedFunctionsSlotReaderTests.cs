using System.Runtime.InteropServices;
using System.Text.Json;

using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Q39 at C1, driven by the committed classic slot registry: a reduced or mutated classic table never lets a slot
///     beyond its declared or physical size be read, a nil slot is reported as nil and never dereferenced, cells are
///     observed afresh on every read, and 64-bit addresses survive (audit A03-21, A03-23..26, AX04-03/09/10, A20-Q39-1).
///     There is no invoking API at all: <see cref="ClassicExportedFunctionsSlotReader" /> only observes.
/// </summary>
public sealed class ClassicExportedFunctionsSlotReaderTests
{
	private const ulong HighAddress = 0xFFFF_8000_0000_0000;

	/// <summary>Every readable slot (1-158) with its registry minimum declared size.</summary>
	public static TheoryData<int, int> RegistrySlots()
	{
		TheoryData<int, int> data = [];
		foreach (JsonElement slot in ClassicSlotRegistry.Slots[1..])
		{
			data.Add(slot.GetProperty("slot").GetInt32(), slot.GetProperty("minDeclaredSize").GetInt32());
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(RegistrySlots))]
	[Trait("Qualification", "Q39")]
	public void Slot_is_refused_when_the_declared_size_is_one_byte_short(int slot, int minDeclaredSize)
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		byte[] table = Table(minDeclaredSize - 1, ClassicExportedFunctionsSlotReader.TableByteCount);
		WriteSlot(table, slot, 0x1234_5678_9ABC_DEF0);

		Assert.False(
			ClassicExportedFunctionsSlotReader.TryReadSlot(table, slot, out ClassicSlotObservation observation));
		Assert.Equal(default, observation);
	}

	[Theory]
	[MemberData(nameof(RegistrySlots))]
	[Trait("Qualification", "Q39")]
	public void Slot_is_read_when_declared_and_physical_sizes_cover_it_exactly(int slot, int minDeclaredSize)
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		Assert.Equal(8 * (slot + 1), minDeclaredSize);
		byte[] table = Table(minDeclaredSize, minDeclaredSize);
		ulong value = 0x0100_0000_0000_0000UL + (ulong) slot;
		WriteSlot(table, slot, value);

		Assert.True(
			ClassicExportedFunctionsSlotReader.TryReadSlot(table, slot, out ClassicSlotObservation observation));
		Assert.Equal(slot, observation.Slot);
		Assert.Equal((nint) value, observation.RawValue);
		Assert.False(observation.IsNull);
	}

	[Theory]
	[InlineData(ClassicExportedFunctionsSlotReader.TableByteCount + 8)]
	[InlineData(4096)]
	[InlineData(int.MaxValue)]
	[Trait("Qualification", "Q39")]
	public void Oversized_declared_table_is_accepted_for_the_known_slots(int declaredSize)
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		byte[] table = Table(declaredSize, ClassicExportedFunctionsSlotReader.TableByteCount + 64);
		for (int slot = 1; slot < ClassicExportedFunctionsSlotReader.SlotCount; slot++)
		{
			WriteSlot(table, slot, (ulong) slot * 0x10);
		}

		for (int slot = 1; slot < ClassicExportedFunctionsSlotReader.SlotCount; slot++)
		{
			Assert.True(
				ClassicExportedFunctionsSlotReader.TryReadSlot(table, slot, out ClassicSlotObservation observation));
			Assert.Equal(slot * 0x10, observation.RawValue);
		}

		// A future suffix is compatible with the known slots, but no slot past the known table is ever read.
		Assert.False(ClassicExportedFunctionsSlotReader.TryReadSlot(table, ClassicExportedFunctionsSlotReader.SlotCount,
			out _));
	}

	[Theory]
	[MemberData(nameof(RegistrySlots))]
	[Trait("Qualification", "Q39")]
	public void Physically_truncated_table_is_refused_even_when_the_declared_size_is_large(int slot,
		int minDeclaredSize)
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		byte[] table = Table(ClassicExportedFunctionsSlotReader.TableByteCount, minDeclaredSize - 1);

		Assert.False(
			ClassicExportedFunctionsSlotReader.TryReadSlot(table, slot, out ClassicSlotObservation observation));
		Assert.Equal(default, observation);
	}

	[Fact]
	[Trait("Qualification", "Q39")]
	public void Null_slot_is_reported_as_null_and_never_dereferenced()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		byte[] table = Table(ClassicExportedFunctionsSlotReader.TableByteCount,
			ClassicExportedFunctionsSlotReader.TableByteCount);
		int[] nilSlots =
		[
			.. ClassicSlotRegistry.Slots
				.Where(static slot => string.Equals(slot.GetProperty("nullability").GetString(), "NilAssigned",
					StringComparison.Ordinal))
				.Select(static slot => slot.GetProperty("slot").GetInt32())
		];
		// A non-nil but invalid address in a neighbouring slot: reading it must not touch the address either.
		WriteSlot(table, 15, 0x1);

		Assert.Equal(11, nilSlots.Length);
		foreach (int slot in nilSlots)
		{
			Assert.True(
				ClassicExportedFunctionsSlotReader.TryReadSlot(table, slot, out ClassicSlotObservation observation));
			Assert.True(observation.IsNull);
			Assert.Equal(0, observation.RawValue);
		}

		Assert.True(ClassicExportedFunctionsSlotReader.TryReadSlot(table, 15, out ClassicSlotObservation invalid));
		Assert.Equal(1, invalid.RawValue);
		Assert.False(invalid.IsNull);
	}

	[Fact]
	[Trait("Qualification", "Q39")]
	public void Cell_contents_changed_between_two_reads_are_two_distinct_observations()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		const int ReadProcessMemorySlot = 18;
		Assert.Equal("FunctionPointerCell",
			ClassicSlotRegistry.Slots[ReadProcessMemorySlot].GetProperty("nature").GetString());
		byte[] table = Table(ClassicExportedFunctionsSlotReader.TableByteCount,
			ClassicExportedFunctionsSlotReader.TableByteCount);

		WriteSlot(table, ReadProcessMemorySlot, 0x0000_7FF0_0000_1000);
		Assert.True(ClassicExportedFunctionsSlotReader.TryReadSlot(table, ReadProcessMemorySlot,
			out ClassicSlotObservation first));
		WriteSlot(table, ReadProcessMemorySlot, 0x0000_7FF0_0000_2000);
		Assert.True(ClassicExportedFunctionsSlotReader.TryReadSlot(table, ReadProcessMemorySlot,
			out ClassicSlotObservation second));

		Assert.Equal(0x0000_7FF0_0000_1000, first.RawValue);
		Assert.Equal(0x0000_7FF0_0000_2000, second.RawValue);
		Assert.NotEqual(first, second);
	}

	[Fact]
	[Trait("Qualification", "Q39")]
	public void Address_above_32_bits_round_trips_through_a_slot()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		byte[] table = Table(ClassicExportedFunctionsSlotReader.TableByteCount,
			ClassicExportedFunctionsSlotReader.TableByteCount);
		WriteSlot(table, 158, HighAddress);

		Assert.True(ClassicExportedFunctionsSlotReader.TryReadSlot(table, 158, out ClassicSlotObservation observation));
		Assert.Equal(HighAddress, (ulong) observation.RawValue);
		Assert.Equal(unchecked((nint) HighAddress), observation.RawValue);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(ClassicExportedFunctionsSlotReader.SlotCount)]
	[InlineData(int.MaxValue)]
	[InlineData(int.MinValue)]
	[Trait("Qualification", "Q39")]
	public void Slot_zero_and_out_of_range_indices_are_refused(int slot)
	{
		byte[] table = Table(int.MaxValue, ClassicExportedFunctionsSlotReader.TableByteCount + 64);

		Assert.False(
			ClassicExportedFunctionsSlotReader.TryReadSlot(table, slot, out ClassicSlotObservation observation));
		Assert.Equal(default, observation);
	}

	[Fact]
	public void Reader_constants_equal_the_registry_contract()
	{
		JsonElement contract = ClassicSlotRegistry.Root.GetProperty("contract");

		Assert.Equal(ClassicExportedFunctionsSlotReader.SlotCount, contract.GetProperty("fieldCount").GetInt32());
		Assert.Equal(ClassicExportedFunctionsSlotReader.TableByteCount,
			contract.GetProperty("x64TableSize").GetInt32());
		Assert.Equal(ClassicExportedFunctionsSlotReader.TableByteCount, ClassicSlotRegistry.MinDeclaredSize(158));
	}

	private static byte[] Table(int declaredSize, int physicalLength)
	{
		byte[] table = new byte[physicalLength];
		MemoryMarshal.Write(table, in declaredSize);
		return table;
	}

	private static void WriteSlot(byte[] table, int slot, ulong value)
	{
		int offset = 8 * slot;
		if (offset + 8 <= table.Length)
		{
			MemoryMarshal.Write(table.AsSpan(offset, 8), in value);
		}
	}
}
