using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Structural and call-shape regressions for the physically contiguous C-header prefix of
///     <c>ExportedFunctions</c>. Conflicting or nullable slots are verified as opaque and are never invoked.
/// </summary>
public sealed unsafe class ExportedFunctionsPrefixTests
{
    [Fact]
    public void ExportedFunctionsPrefix_on_64_bit_matches_the_installed_C_header_layout()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        ExportedFunctionsPrefix exports = default;
        void* origin = &exports;

        Assert.Equal(144, Layout.SizeOf<ExportedFunctionsPrefix>());
        Assert.Equal(0, Layout.OffsetOf(origin, &exports.SizeOfExportedFunctions));
        Assert.Equal(8, Layout.OffsetOf(origin, &exports.ShowMessage));
        Assert.Equal(16, Layout.OffsetOf(origin, &exports.RegisterFunction));
        Assert.Equal(24, Layout.OffsetOf(origin, &exports.UnregisterFunction));
        Assert.Equal(32, Layout.OffsetOf(origin, &exports.OpenedProcessId));
        Assert.Equal(40, Layout.OffsetOf(origin, &exports.OpenedProcessHandle));
        Assert.Equal(48, Layout.OffsetOf(origin, &exports.GetMainWindowHandle));
        Assert.Equal(56, Layout.OffsetOf(origin, &exports.AutoAssemble));
        Assert.Equal(64, Layout.OffsetOf(origin, &exports.Assembler));
        Assert.Equal(72, Layout.OffsetOf(origin, &exports.Disassembler));
        Assert.Equal(80, Layout.OffsetOf(origin, &exports.ChangeRegistersAtAddress));
        Assert.Equal(88, Layout.OffsetOf(origin, &exports.InjectDll));
        Assert.Equal(96, Layout.OffsetOf(origin, &exports.FreezeMemory));
        Assert.Equal(104, Layout.OffsetOf(origin, &exports.UnfreezeMemory));
        Assert.Equal(112, Layout.OffsetOf(origin, &exports.FixMemory));
        Assert.Equal(120, Layout.OffsetOf(origin, &exports.ProcessList));
        Assert.Equal(128, Layout.OffsetOf(origin, &exports.ReloadSettings));
        Assert.Equal(136, Layout.OffsetOf(origin, &exports.GetAddressFromPointer));
    }

    [Fact]
    public void Direct_function_slots_are_explicitly_stdcall()
    {
        var fields = typeof(ExportedFunctionsPrefix).GetFields(BindingFlags.Instance | BindingFlags.Public);
        var functionPointerCount = 0;

        foreach (var field in fields)
        {
            var fieldType = field.GetModifiedFieldType();
            if (!fieldType.UnderlyingSystemType.IsFunctionPointer) continue;

            functionPointerCount++;
            var conventions = fieldType.GetFunctionPointerCallingConventions();
            var convention = Assert.Single(conventions);
            Assert.Equal(typeof(CallConvStdcall), convention);
        }

        Assert.Equal(13, functionPointerCount);
    }

    [Fact]
    public void RegisterFunction_and_ChangeRegistersAtAddress_accept_the_declared_stdcall_shapes()
    {
        ExportedFunctionsPrefix exports = default;
        exports.RegisterFunction = &FakeRegisterFunction;
        exports.ChangeRegistersAtAddress = &FakeChangeRegistersAtAddress;
        RegisterModificationInfo request = default;
        request.ChangeR15 = Bool32.True;

        Assert.Equal(56, exports.RegisterFunction(42, PluginType.AutoAssembler, (void*)6));
        Assert.True(exports.ChangeRegistersAtAddress(0x1234, &request).IsTrue);
        Assert.Equal((nuint)0x123C, request.NewR15);
    }

    [Fact]
    public void Prefix_stops_before_the_pointer_to_pointer_hook_suffix()
    {
        var fields = typeof(ExportedFunctionsPrefix).GetFields(BindingFlags.Instance | BindingFlags.Public);

        Assert.Null(typeof(ExportedFunctionsPrefix).GetField("ReadProcessMemory",
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Historically_null_and_conflicting_slots_stay_opaque()
    {
        var fixMemory = typeof(ExportedFunctionsPrefix).GetField(nameof(ExportedFunctionsPrefix.FixMemory),
            BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new InvalidOperationException("The FixMemory field was not found.");
        var getAddressFromPointer = typeof(ExportedFunctionsPrefix).GetField(
                                        nameof(ExportedFunctionsPrefix.GetAddressFromPointer),
                                        BindingFlags.Instance | BindingFlags.Public)
                                    ?? throw new InvalidOperationException("The GetAddressFromPointer field was not found.");

        var fixMemoryType = fixMemory.GetModifiedFieldType().UnderlyingSystemType;
        var getAddressFromPointerType = getAddressFromPointer.GetModifiedFieldType().UnderlyingSystemType;

        Assert.True(fixMemoryType.IsPointer);
        Assert.Equal(typeof(void), fixMemoryType.GetElementType());
        Assert.True(getAddressFromPointerType.IsPointer);
        Assert.Equal(typeof(void), getAddressFromPointerType.GetElementType());
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FakeRegisterFunction(int pluginId, PluginType functionType, void* initializationRecord)
    {
        return pluginId + (int)functionType + (int)(nint)initializationRecord;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 FakeChangeRegistersAtAddress(nuint address, RegisterModificationInfo* changes)
    {
        if (changes is null || !changes->ChangeR15.IsTrue) return Bool32.False;

        changes->NewR15 = address + 8;
        return Bool32.True;
    }
}
