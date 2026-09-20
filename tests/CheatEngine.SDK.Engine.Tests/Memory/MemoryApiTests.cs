using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Memory;

/// <summary>Fixture tests for the target and host memory surfaces using CE 7.7-shaped Lua globals.</summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryApiTests
{
    private static ReadOnlySpan<byte> TargetStandIn => """
                                                       local target = {
                                                         byte = {[16] = 255},
                                                         word = {[17] = 65534},
                                                         dword = {[18] = 4294967294},
                                                         qword = {[19] = -2},
                                                         pointer = {[20] = -16},
                                                         single = {[21] = 1.5},
                                                         double = {[22] = 3.25},
                                                         bytes = {[32] = {3, 1, 4, 1}},
                                                         text = {[48] = "target-text"},
                                                       }
                                                       local function signed(v, width)
                                                         local top = 2 ^ (width - 1)
                                                         local range = 2 ^ width
                                                         return v >= top and v - range or v
                                                       end
                                                       function readByte(a) return target.byte[a] end
                                                       function readSmallInteger(a, s)
                                                         local v = target.word[a]
                                                         if v == nil then return nil end
                                                         return s and signed(v, 16) or v
                                                       end
                                                       function readInteger(a, s)
                                                         local v = target.dword[a]
                                                         if v == nil then return nil end
                                                         return s and signed(v, 32) or v
                                                       end
                                                       function readQword(a) return target.qword[a] end
                                                       function readPointer(a) return target.pointer[a] end
                                                       function readFloat(a) return target.single[a] end
                                                       function readDouble(a) return target.double[a] end
                                                       function readString(a, _, _) return target.text[a] end
                                                       function readBytes(a, count, asTable)
                                                         local source = target.bytes[a]
                                                         if source == nil then return nil end
                                                         local result = {}
                                                         for i = 1, count do
                                                           if source[i] == nil then return nil end
                                                           result[i] = source[i]
                                                         end
                                                         return asTable and result or table.unpack(result)
                                                       end
                                                       function writeByte(a, v) target.byte[a] = v; return true end
                                                       function writeSmallInteger(a, v) target.word[a] = v % 65536; return true end
                                                       function writeInteger(a, v) target.dword[a] = v % 4294967296; return a ~= 57005 end
                                                       function writeQword(a, v) target.qword[a] = v; return true end
                                                       function writePointer(a, v) target.pointer[a] = v; return true end
                                                       function writeFloat(a, v) target.single[a] = v; return true end
                                                       function writeDouble(a, v) target.double[a] = v; return true end
                                                       function writeString(a, v, _) target.text[a] = v; return true end
                                                       function writeBytes(a, values)
                                                         local copy = {}
                                                         for i = 1, #values do copy[i] = values[i] end
                                                         target.bytes[a] = copy
                                                         if a == 34 then return #values - 1 end
                                                         if a == 35 then return 0 end
                                                         return #values
                                                       end
                                                       """u8;

    private static ReadOnlySpan<byte> HostStandIn => """
                                                     local host = {
                                                       word = {[65] = 65534},
                                                       dword = {[66] = 4294967294},
                                                       qword = {[67] = -2},
                                                       pointer = {[68] = -32},
                                                       single = {[69] = 2.5},
                                                       double = {[70] = 6.5},
                                                       bytes = {[64] = {255}, [80] = {9, 8, 7}},
                                                       text = {[96] = "host-text"},
                                                     }
                                                     local function signed(v, width)
                                                       local top = 2 ^ (width - 1)
                                                       local range = 2 ^ width
                                                       return v >= top and v - range or v
                                                     end
                                                     function readBytesLocal(a, count, asTable)
                                                       local source = host.bytes[a]
                                                       if source == nil then return nil end
                                                       local result = {}
                                                       for i = 1, count do
                                                         if source[i] == nil then return nil end
                                                         result[i] = source[i]
                                                       end
                                                       return asTable and result or table.unpack(result)
                                                     end
                                                     function readSmallIntegerLocal(a, s)
                                                       local v = host.word[a]
                                                       if v == nil then return nil end
                                                       return s and signed(v, 16) or v
                                                     end
                                                     function readIntegerLocal(a, s)
                                                       local v = host.dword[a]
                                                       if v == nil then return nil end
                                                       return s and signed(v, 32) or v
                                                     end
                                                     function readQwordLocal(a) return host.qword[a] end
                                                     function readPointerLocal(a) return host.pointer[a] end
                                                     function readFloatLocal(a) return host.single[a] end
                                                     function readDoubleLocal(a) return host.double[a] end
                                                     function readStringLocal(a, _, _) return host.text[a] end
                                                     function writeBytesLocal(a, values)
                                                       local copy = {}
                                                       for i = 1, #values do copy[i] = values[i] end
                                                       host.bytes[a] = copy
                                                       if a == 82 then return #values - 1 end
                                                       if a == 83 then return 0 end
                                                       return #values
                                                     end
                                                     function writeSmallIntegerLocal(a, v) host.word[a] = v % 65536; return true end
                                                     function writeIntegerLocal(a, v) host.dword[a] = v % 4294967296; return true end
                                                     function writeQwordLocal(a, v) host.qword[a] = v; return true end
                                                     function writePointerLocal(a, v) host.pointer[a] = v; return true end
                                                     function writeFloatLocal(a, v) host.single[a] = v; return true end
                                                     function writeDoubleLocal(a, v) host.double[a] = v; return true end
                                                     function writeStringLocal(a, v, _) host.text[a] = v; return true end
                                                     """u8;

    [Fact]
    public void Target_scalars_preserve_signedness_widths_pointer_bits_and_floating_point_values()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State, TargetStandIn);

        Assert.True(TargetMemory.TryReadUInt8(16UL, out var u8, out var failure));
        Assert.Equal(MemoryAccessFailure.None, failure);
        Assert.Equal(byte.MaxValue, u8);
        Assert.True(TargetMemory.TryReadInt8(16UL, out var i8, out failure));
        Assert.Equal(-1, i8);
        Assert.True(TargetMemory.TryReadUInt16(17UL, out var u16, out failure));
        Assert.Equal(ushort.MaxValue - 1, u16);
        Assert.True(TargetMemory.TryReadInt16(17UL, out var i16, out failure));
        Assert.Equal(-2, i16);
        Assert.True(TargetMemory.TryReadUInt32(18UL, out var u32, out failure));
        Assert.Equal(uint.MaxValue - 1, u32);
        Assert.True(TargetMemory.TryReadInt32(18UL, out var i32, out failure));
        Assert.Equal(-2, i32);
        Assert.True(TargetMemory.TryReadUInt64(19UL, out var u64, out failure));
        Assert.Equal(ulong.MaxValue - 1, u64);
        Assert.True(TargetMemory.TryReadInt64(19UL, out var i64, out failure));
        Assert.Equal(-2, i64);
        Assert.True(TargetMemory.TryReadPointer(20UL, out var pointer, out failure));
        Assert.Equal(ulong.MaxValue - 15, pointer.Value);
        Assert.True(TargetMemory.TryReadSingle(21UL, out var single, out failure));
        Assert.Equal(1.5F, single);
        Assert.True(TargetMemory.TryReadDouble(22UL, out var @double, out failure));
        Assert.Equal(3.25, @double);

        Assert.True(TargetMemory.TryWriteInt8(16UL, -7, out failure));
        Assert.True(TargetMemory.TryWriteUInt16(17UL, 123, out failure));
        Assert.True(TargetMemory.TryWriteInt16(17UL, -2, out failure));
        Assert.True(TargetMemory.TryWriteUInt32(18UL, uint.MaxValue, out failure));
        Assert.True(TargetMemory.TryWriteInt32(18UL, -2, out failure));
        Assert.True(TargetMemory.TryWriteUInt64(19UL, ulong.MaxValue, out failure));
        Assert.True(TargetMemory.TryWriteInt64(19UL, -2, out failure));
        Assert.True(TargetMemory.TryWritePointer(20UL, 0x1234UL, out failure));
        Assert.True(TargetMemory.TryWriteSingle(21UL, 2.5F, out failure));
        Assert.True(TargetMemory.TryWriteDouble(22UL, 7.5, out failure));
        Assert.True(TargetMemory.TryReadPointer(20UL, out pointer, out failure));
        Assert.Equal(0x1234UL, pointer.Value);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Target_buffers_and_strings_keep_their_order_and_never_return_a_dangling_lua_span()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State, TargetStandIn);

        Span<byte> bytes = stackalloc byte[4];
        Assert.True(TargetMemory.TryReadBytes(32UL, bytes, out var failure));
        Assert.Equal(MemoryAccessFailure.None, failure);
        Assert.True(bytes.SequenceEqual(new byte[] { 3, 1, 4, 1 }));
        Assert.True(TargetMemory.TryWriteBytes(33UL, [2, 7, 1, 8], out failure));
        bytes.Clear();
        Assert.True(TargetMemory.TryReadBytes(33UL, bytes, out failure));
        Assert.True(bytes.SequenceEqual(new byte[] { 2, 7, 1, 8 }));

        Span<byte> utf8 = stackalloc byte[16];
        Assert.True(TargetMemory.TryReadUtf8(48UL, 100, utf8, wideCharacter: false, out var written, out failure));
        Assert.True(utf8[..written].SequenceEqual("target-text"u8));
        Assert.True(TargetMemory.TryReadString(48UL, 100, wideCharacter: false, out var text, out failure));
        Assert.Equal("target-text", text);
        Assert.True(TargetMemory.TryWriteUtf8(49UL, "updated"u8, wideCharacter: false, out failure));
        Assert.True(TargetMemory.TryReadString(49UL, 100, wideCharacter: false, out text, out failure));
        Assert.Equal("updated", text);
        Assert.True(TargetMemory.TryWriteString(50UL, "text path".AsSpan(), wideCharacter: false, out failure));
        Assert.True(TargetMemory.TryReadString(50UL, 100, wideCharacter: false, out text, out failure));
        Assert.Equal("text path", text);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Byte_writes_require_the_full_CE_count_for_target_and_host_memory()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State, TargetStandIn);
        EngineTest.Run(scope.State, HostStandIn);

        ReadOnlySpan<byte> payload = [0, 1, 255, 42];
        Assert.True(TargetMemory.TryWriteBytes(33UL, payload, out var failure));
        Assert.Equal(MemoryAccessFailure.None, failure);
        Assert.False(TargetMemory.TryWriteBytes(34UL, payload, out failure));
        Assert.Equal(MemoryAccessFailure.WriteFailed, failure);
        Assert.False(TargetMemory.TryWriteBytes(35UL, payload, out failure));
        Assert.Equal(MemoryAccessFailure.WriteFailed, failure);

        Assert.True(HostMemory.TryWriteBytes(new HostAddress(81), payload, out failure));
        Assert.Equal(MemoryAccessFailure.None, failure);
        Assert.False(HostMemory.TryWriteBytes(new HostAddress(82), payload, out failure));
        Assert.Equal(MemoryAccessFailure.WriteFailed, failure);
        Assert.False(HostMemory.TryWriteBytes(new HostAddress(83), payload, out failure));
        Assert.Equal(MemoryAccessFailure.WriteFailed, failure);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Empty_byte_writes_do_not_resolve_or_invoke_CE_globals()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State,
            "function writeBytes(_) error('must not run') end function writeBytesLocal(_) error('must not run') end"u8);

        Assert.Equal(0, FakeHost.ProviderCalls);
        Assert.True(TargetMemory.TryWriteBytes(1UL, [], out var failure));
        Assert.Equal(MemoryAccessFailure.None, failure);
        Assert.Equal(1, FakeHost.ProviderCalls);
        Assert.True(HostMemory.TryWriteBytes(new HostAddress(1), [], out failure));
        Assert.Equal(MemoryAccessFailure.None, failure);
        Assert.Equal(2, FakeHost.ProviderCalls);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Empty_byte_writes_preserve_detached_runtime_admission()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using (HostScope scope = new(state))
        {
        }

        Assert.Throws<InvalidOperationException>(() => TargetMemory.TryWriteBytes(1UL, [], out _));
        Assert.Throws<InvalidOperationException>(() => HostMemory.TryWriteBytes(new HostAddress(1), [], out _));
    }

    [Fact]
    [SuppressMessage("Meziantou.Analyzer", "MA0051",
        Justification =
            "The host scalar contract is intentionally exercised end to end in one table-shaped fixture test.")]
    public void Host_scalars_use_host_addresses_and_the_documented_local_byte_table_for_8_bit_access()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State, HostStandIn);

        HostAddress byteAddress = new(64);
        Assert.True(HostMemory.TryReadUInt8(byteAddress, out var u8, out var failure));
        Assert.Equal(byte.MaxValue, u8);
        Assert.True(HostMemory.TryReadInt8(byteAddress, out var i8, out failure));
        Assert.Equal(-1, i8);
        Assert.True(HostMemory.TryReadUInt16(new HostAddress(65), out var u16, out failure));
        Assert.Equal(ushort.MaxValue - 1, u16);
        Assert.True(HostMemory.TryReadInt16(new HostAddress(65), out var i16, out failure));
        Assert.Equal(-2, i16);
        Assert.True(HostMemory.TryReadUInt32(new HostAddress(66), out var u32, out failure));
        Assert.Equal(uint.MaxValue - 1, u32);
        Assert.True(HostMemory.TryReadInt32(new HostAddress(66), out var i32, out failure));
        Assert.Equal(-2, i32);
        Assert.True(HostMemory.TryReadUInt64(new HostAddress(67), out var u64, out failure));
        Assert.Equal(ulong.MaxValue - 1, u64);
        Assert.True(HostMemory.TryReadInt64(new HostAddress(67), out var i64, out failure));
        Assert.Equal(-2, i64);
        Assert.True(HostMemory.TryReadPointer(new HostAddress(68), out var pointer, out failure));
        Assert.Equal(unchecked((nuint)(-32)), pointer.Value);
        Assert.True(HostMemory.TryReadSingle(new HostAddress(69), out var single, out failure));
        Assert.Equal(2.5F, single);
        Assert.True(HostMemory.TryReadDouble(new HostAddress(70), out var @double, out failure));
        Assert.Equal(6.5, @double);

        Assert.True(HostMemory.TryWriteUInt8(byteAddress, 7, out failure));
        Assert.True(HostMemory.TryReadUInt8(byteAddress, out u8, out failure));
        Assert.Equal(7, u8);
        Assert.True(HostMemory.TryWritePointer(new HostAddress(68), new HostAddress(0x5678), out failure));
        Assert.True(HostMemory.TryReadPointer(new HostAddress(68), out pointer, out failure));
        Assert.Equal((nuint)0x5678, pointer.Value);
        Assert.True(HostMemory.TryWriteDouble(new HostAddress(70), 8.5, out failure));
        Assert.True(HostMemory.TryReadDouble(new HostAddress(70), out @double, out failure));
        Assert.Equal(8.5, @double);

        Span<byte> bytes = stackalloc byte[3];
        Assert.True(HostMemory.TryReadBytes(new HostAddress(80), bytes, out failure));
        Assert.True(bytes.SequenceEqual(new byte[] { 9, 8, 7 }));
        Assert.True(HostMemory.TryWriteBytes(new HostAddress(81), [6, 2, 6], out failure));
        Assert.True(HostMemory.TryReadBytes(new HostAddress(81), bytes, out failure));
        Assert.True(bytes.SequenceEqual(new byte[] { 6, 2, 6 }));
        Assert.True(HostMemory.TryReadString(new HostAddress(96), 100, wideCharacter: false, out var text, out failure));
        Assert.Equal("host-text", text);
        Assert.True(HostMemory.TryWriteUtf8(new HostAddress(97), "host-update"u8, wideCharacter: false, out failure));
        Assert.True(HostMemory.TryReadString(new HostAddress(97), 100, wideCharacter: false, out text, out failure));
        Assert.Equal("host-update", text);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Failures_are_classified_without_leaking_stack_values()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State, TargetStandIn);

        Assert.False(TargetMemory.TryReadUInt32(999UL, out _, out var failure));
        Assert.Equal(MemoryAccessFailure.ReadFailed, failure);
        Assert.False(TargetMemory.TryWriteUInt32(57005UL, 1, out failure));
        Assert.Equal(MemoryAccessFailure.WriteFailed, failure);
        EngineTest.Run(scope.State, "function readDouble(_) error('fixture failure') end"u8);
        Assert.False(TargetMemory.TryReadDouble(22UL, out _, out failure));
        Assert.Equal(MemoryAccessFailure.LuaError, failure);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void A_missing_global_is_distinct_from_a_detached_runtime()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using (HostScope scope = new(state))
        {
            Assert.False(TargetMemory.TryReadPointer(1UL, out _, out var failure));
            Assert.Equal(MemoryAccessFailure.GlobalUnavailable, failure);
            Assert.Equal(0, scope.State.Top);
        }

        Assert.Throws<InvalidOperationException>(() => TargetMemory.TryReadPointer(1UL, out _, out _));
    }

    [Fact]
    public void Host_address_does_not_implicitly_cross_the_target_address_space()
    {
        HostAddress address = new(0x1234);
        Assert.Equal((nuint)0x1234, address.Value);
        Assert.Equal(new HostAddress(0x1234), address);
        Assert.NotEqual(new HostAddress(0x1235), address);
        Assert.Equal(IntPtr.Size == 8 ? "0000000000001234" : "00001234",
            address.ToString(IntPtr.Size == 8 ? "X16" : "X8", CultureInfo.InvariantCulture));
    }
}
