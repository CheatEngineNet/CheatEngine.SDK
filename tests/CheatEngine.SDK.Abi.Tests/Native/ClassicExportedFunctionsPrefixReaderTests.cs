using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi.Native;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Boundary tests for copying the physically qualified classic table prefix without invoking any host callback.
/// </summary>
public sealed unsafe class ClassicExportedFunctionsPrefixReaderTests
{
	[Fact]
	[Trait("Qualification", "Q39")]
	public void TryCopy_rejects_an_empty_table_representation()
	{
		bool copied =
			ClassicExportedFunctionsPrefixReader.TryCopy(ReadOnlySpan<byte>.Empty, out ExportedFunctionsPrefix prefix);

		Assert.False(copied);
		Assert.Equal(default, prefix);
	}

	[Fact]
	[Trait("Qualification", "Q39")]
	public void TryCopy_rejects_a_buffer_that_cannot_contain_the_declared_size_field()
	{
		Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DeclaredSizeByteCount - 1];

		bool copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out ExportedFunctionsPrefix prefix);

		Assert.False(copied);
		Assert.Equal(default, prefix);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount - 1)]
	[Trait("Qualification", "Q39")]
	public void TryCopy_rejects_a_truncated_declared_table(int declaredSize)
	{
		Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount];
		WriteDeclaredSize(table, declaredSize);

		bool copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out ExportedFunctionsPrefix prefix);

		Assert.False(copied);
		Assert.Equal(default, prefix);
	}

	[Fact]
	[Trait("Qualification", "Q39")]
	public void TryCopy_rejects_a_physically_truncated_table_even_when_its_size_claim_is_sufficient()
	{
		Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount - 1];
		WriteDeclaredSize(table, ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount);

		bool copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out ExportedFunctionsPrefix prefix);

		Assert.False(copied);
		Assert.Equal(default, prefix);
	}

	[Theory]
	[InlineData(ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount)]
	[InlineData(int.MaxValue)]
	[Trait("Qualification", "Q39")]
	public void TryCopy_copies_exactly_the_qualified_prefix_without_overflow(int declaredSize)
	{
		uint processId = 0x2468u;
		void* processHandle = (void*) 0x1234_5678;
		ExportedFunctionsPrefix expected = default;
		expected.SizeOfExportedFunctions = declaredSize;
		expected.ShowMessage = &FakeShowMessage;
		expected.OpenedProcessId = &processId;
		expected.OpenedProcessHandle = &processHandle;
		expected.FixMemory = null;
		expected.GetAddressFromPointer = (void*) 0x55AA;

		Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount];
		MemoryMarshal.Write(table, in expected);

		bool copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out ExportedFunctionsPrefix actual);

		Assert.True(copied);
		Assert.Equal(declaredSize, actual.SizeOfExportedFunctions);
		Assert.Equal((nint) expected.ShowMessage, (nint) actual.ShowMessage);
		Assert.Equal(processId, *actual.OpenedProcessId);
		Assert.Equal((nint) processHandle, (nint) (*actual.OpenedProcessHandle));
		Assert.Equal(0, (nint) actual.FixMemory);
		Assert.Equal(0x55AA, (nint) actual.GetAddressFromPointer);
	}

	/// <summary>
	///     For every slot N of the prefix, a table whose declared size stops one byte short of slot N's end
	///     (<c>8 * (N + 1) - 1</c>; slot 0 is 4 bytes wide, so 7 covers it) is refused as a whole: the prefix is never
	///     partially copied.
	/// </summary>
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	[InlineData(6)]
	[InlineData(7)]
	[InlineData(8)]
	[InlineData(9)]
	[InlineData(10)]
	[InlineData(11)]
	[InlineData(12)]
	[InlineData(13)]
	[InlineData(14)]
	[InlineData(15)]
	[InlineData(16)]
	[InlineData(17)]
	[Trait("Qualification", "Q39")]
	public void TryCopy_rejects_every_declared_size_below_each_prefix_slot_boundary(int slot)
	{
		Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount];
		table.Fill(0xA5);
		WriteDeclaredSize(table, (8 * (slot + 1)) - 1);

		bool copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out ExportedFunctionsPrefix prefix);

		Assert.False(copied);
		Assert.Equal(default, prefix);
	}

	/// <summary>
	///     A table that declares 64 bytes covers slots 0-7 exactly, yet the prefix reader copies nothing: it is all or
	///     nothing by design, and per-slot reads go through <c>ClassicExportedFunctionsSlotReader</c>.
	/// </summary>
	[Theory]
	[InlineData(64)]
	[InlineData(136)]
	[InlineData(143)]
	[Trait("Qualification", "Q39")]
	public void TryCopy_is_all_or_nothing_between_slot_boundaries(int declaredSize)
	{
		Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount];
		table.Fill(0x5A);
		WriteDeclaredSize(table, declaredSize);

		bool copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out ExportedFunctionsPrefix prefix);

		Assert.False(copied);
		Assert.Equal(default, prefix);
		Assert.Equal(0, (nint) prefix.ShowMessage);
		Assert.Equal(0, prefix.SizeOfExportedFunctions);
	}

	[Fact]
	public void Prefix_distinguishes_direct_function_slots_value_cells_and_opaque_null_slots_without_invocation()
	{
		FieldInfo showMessage = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.ShowMessage))
		                        ?? throw new InvalidOperationException("The ShowMessage field was not found.");
		FieldInfo processId = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.OpenedProcessId))
		                      ?? throw new InvalidOperationException("The OpenedProcessId field was not found.");
		FieldInfo processHandle =
			typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.OpenedProcessHandle))
			?? throw new InvalidOperationException("The OpenedProcessHandle field was not found.");
		FieldInfo fixMemory = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.FixMemory))
		                      ?? throw new InvalidOperationException("The FixMemory field was not found.");
		FieldInfo getAddress =
			typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.GetAddressFromPointer))
			?? throw new InvalidOperationException("The GetAddressFromPointer field was not found.");

		Type showMessageType = showMessage.GetModifiedFieldType().UnderlyingSystemType;
		Type processIdType = processId.GetModifiedFieldType().UnderlyingSystemType;
		Type processHandleType = processHandle.GetModifiedFieldType().UnderlyingSystemType;
		Type fixMemoryType = fixMemory.GetModifiedFieldType().UnderlyingSystemType;
		Type getAddressType = getAddress.GetModifiedFieldType().UnderlyingSystemType;

		Assert.True(showMessageType.IsFunctionPointer);
		Assert.True(processIdType.IsPointer);
		Assert.True(processHandleType.IsPointer);
		Assert.Equal(typeof(uint), processIdType.GetElementType());
		Assert.True(processHandleType.GetElementType()?.IsPointer);
		Assert.Equal(typeof(void), fixMemoryType.GetElementType());
		Assert.Equal(typeof(void), getAddressType.GetElementType());
	}

	[Fact]
	public void Prefix_stops_before_the_hookable_pointer_cell_suffix()
	{
		Assert.Null(typeof(ExportedFunctionsPrefix).GetField("ReadProcessMemory",
			BindingFlags.Instance | BindingFlags.Public));
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void FakeShowMessage(byte* message)
	{
		_ = message;
	}

	private static void WriteDeclaredSize(Span<byte> table, int value)
	{
		MemoryMarshal.Write(table, in value);
	}
}
