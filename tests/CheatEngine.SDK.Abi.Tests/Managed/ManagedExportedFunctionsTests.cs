using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Managed;

public sealed unsafe class ManagedExportedFunctionsTests
{
    private static readonly void* FakeLuaState = (void*)0x5150;

    private static nint s_pushedState;
    private static nint s_pushedObject;
    private static int s_processMessagesCalls;

    [Fact]
    public void Size_on_64_bit_is_48_bytes()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

        Assert.Equal(48, Layout.SizeOf<ManagedExportedFunctions>());
    }

    [Fact]
    public void Field_offsets_on_64_bit_match_the_host_record()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        ManagedExportedFunctions exports = default;
        void* origin = &exports;

        Assert.Equal(0, Layout.OffsetOf(origin, &exports.SizeOfExportedFunctions));
        Assert.Equal(8, Layout.OffsetOf(origin, &exports.GetLuaState));
        Assert.Equal(16, Layout.OffsetOf(origin, &exports.LuaRegister));
        Assert.Equal(24, Layout.OffsetOf(origin, &exports.LuaPushClassInstance));
        Assert.Equal(32, Layout.OffsetOf(origin, &exports.ProcessMessages));
        Assert.Equal(40, Layout.OffsetOf(origin, &exports.CheckSynchronize));
    }

    /// <summary>
    ///     Builds the record the way the host does (an integer followed by five pointer-sized slots, written as raw
    ///     bytes), then reads it through the structure. Independent of the address-of arithmetic used above.
    /// </summary>
    [Fact]
    public void Overlay_on_a_raw_host_record_on_64_bit_reads_every_slot()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        var raw = stackalloc byte[48];
        new Span<byte>(raw, 48).Fill(0xCC);
        Unsafe.WriteUnaligned(raw, 48);
        for (var slot = 1; slot <= 5; slot++) Unsafe.WriteUnaligned(raw + slot * 8, (ulong)slot * 0x1000_0000_1000UL);

        var copy = *(ManagedExportedFunctions*)raw;

        Assert.Equal(48, copy.SizeOfExportedFunctions);
        Assert.Equal(0x1000_0000_1000UL, (ulong)copy.GetLuaState);
        Assert.Equal(0x2000_0000_2000UL, (ulong)copy.LuaRegister);
        Assert.Equal(0x3000_0000_3000UL, (ulong)copy.LuaPushClassInstance);
        Assert.Equal(0x4000_0000_4000UL, (ulong)copy.ProcessMessages);
        Assert.Equal(0x5000_0000_5000UL, (ulong)copy.CheckSynchronize);
    }

    /// <summary>
    ///     Plays the host: every typed slot points at a real stdcall function with the documented shape and is
    ///     invoked through the record.
    /// </summary>
    [Fact]
    public void Typed_slots_are_callable_with_the_declared_shapes()
    {
        ManagedExportedFunctions exports = default;
        exports.SizeOfExportedFunctions = Layout.SizeOf<ManagedExportedFunctions>();
        exports.GetLuaState = &FakeGetLuaState;
        exports.LuaPushClassInstance = &FakePushClassInstance;
        exports.ProcessMessages = &FakeProcessMessages;
        exports.CheckSynchronize = &FakeCheckSynchronize;

        var state = exports.GetLuaState();
        Assert.Equal((nint)FakeLuaState, (nint)state);

        exports.LuaPushClassInstance(state, (void*)0x7777);
        Assert.Equal((nint)FakeLuaState, s_pushedState);
        Assert.Equal(0x7777, s_pushedObject);

        var before = s_processMessagesCalls;
        exports.ProcessMessages();
        Assert.Equal(before + 1, s_processMessagesCalls);

        Assert.True(exports.CheckSynchronize(42).IsTrue);
        Assert.False(exports.CheckSynchronize(0).IsTrue);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* FakeGetLuaState()
    {
        return FakeLuaState;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void FakePushClassInstance(void* state, void* instance)
    {
        s_pushedState = (nint)state;
        s_pushedObject = (nint)instance;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void FakeProcessMessages()
    {
        s_processMessagesCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool8 FakeCheckSynchronize(int timeout)
    {
        return timeout == 42;
    }
}
