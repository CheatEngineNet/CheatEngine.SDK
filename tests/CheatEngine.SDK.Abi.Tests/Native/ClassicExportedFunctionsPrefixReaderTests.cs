using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using CheatEngine.SDK.Abi.Native;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Boundary tests for copying the physically qualified classic table prefix without invoking any host callback.
/// </summary>
public sealed unsafe class ClassicExportedFunctionsPrefixReaderTests
{
    [Fact]
    public void TryCopy_rejects_an_empty_table_representation()
    {
        var copied = ClassicExportedFunctionsPrefixReader.TryCopy(ReadOnlySpan<byte>.Empty, out var prefix);

        Assert.False(copied);
        Assert.Equal(default, prefix);
    }

    [Fact]
    public void TryCopy_rejects_a_buffer_that_cannot_contain_the_declared_size_field()
    {
        Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DeclaredSizeByteCount - 1];

        var copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out var prefix);

        Assert.False(copied);
        Assert.Equal(default, prefix);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount - 1)]
    public void TryCopy_rejects_a_truncated_declared_table(int declaredSize)
    {
        Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount];
        WriteDeclaredSize(table, declaredSize);

        var copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out var prefix);

        Assert.False(copied);
        Assert.Equal(default, prefix);
    }

    [Fact]
    public void TryCopy_rejects_a_physically_truncated_table_even_when_its_size_claim_is_sufficient()
    {
        Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount - 1];
        WriteDeclaredSize(table, ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount);

        var copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out var prefix);

        Assert.False(copied);
        Assert.Equal(default, prefix);
    }

    [Theory]
    [InlineData(ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount)]
    [InlineData(int.MaxValue)]
    public void TryCopy_copies_exactly_the_qualified_prefix_without_overflow(int declaredSize)
    {
        var processId = 0x2468u;
        void* processHandle = (void*)0x1234_5678;
        ExportedFunctionsPrefix expected = default;
        expected.SizeOfExportedFunctions = declaredSize;
        expected.ShowMessage = &FakeShowMessage;
        expected.OpenedProcessId = &processId;
        expected.OpenedProcessHandle = &processHandle;
        expected.FixMemory = null;
        expected.GetAddressFromPointer = (void*)0x55AA;

        Span<byte> table = stackalloc byte[ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount];
        MemoryMarshal.Write(table, in expected);

        var copied = ClassicExportedFunctionsPrefixReader.TryCopy(table, out var actual);

        Assert.True(copied);
        Assert.Equal(declaredSize, actual.SizeOfExportedFunctions);
        Assert.Equal((nint)expected.ShowMessage, (nint)actual.ShowMessage);
        Assert.Equal(processId, *actual.OpenedProcessId);
        Assert.Equal((nint)processHandle, (nint)(*actual.OpenedProcessHandle));
        Assert.Equal((nint)0, (nint)actual.FixMemory);
        Assert.Equal((nint)0x55AA, (nint)actual.GetAddressFromPointer);
    }

    [Fact]
    public void Prefix_distinguishes_direct_function_slots_value_cells_and_opaque_null_slots_without_invocation()
    {
        var showMessage = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.ShowMessage))
            ?? throw new InvalidOperationException("The ShowMessage field was not found.");
        var processId = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.OpenedProcessId))
            ?? throw new InvalidOperationException("The OpenedProcessId field was not found.");
        var processHandle = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.OpenedProcessHandle))
            ?? throw new InvalidOperationException("The OpenedProcessHandle field was not found.");
        var fixMemory = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.FixMemory))
            ?? throw new InvalidOperationException("The FixMemory field was not found.");
        var getAddress = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.GetAddressFromPointer))
            ?? throw new InvalidOperationException("The GetAddressFromPointer field was not found.");

        var showMessageType = showMessage.GetModifiedFieldType().UnderlyingSystemType;
        var processIdType = processId.GetModifiedFieldType().UnderlyingSystemType;
        var processHandleType = processHandle.GetModifiedFieldType().UnderlyingSystemType;
        var fixMemoryType = fixMemory.GetModifiedFieldType().UnderlyingSystemType;
        var getAddressType = getAddress.GetModifiedFieldType().UnderlyingSystemType;

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
