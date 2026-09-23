using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Memory;

/// <summary>
///     Qualification Q20 at C2 for the memory text and byte surfaces: embedded NULs and invalid UTF-8 keep their exact
///     bytes in the byte forms, <c>maximumLength</c> and the wide flag reach Cheat Engine's globals unchanged, and a
///     partial byte read reports its confirmed prefix (audit A12-11, A12-15, A12-16). The stand-ins record what they
///     receive; the unit CE gives <c>maximumLength</c> for a wide read is not qualified here (C3).
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryTextFidelityTests
{
	private static ReadOnlySpan<byte> StandIn => """
	                                             calls = {}
	                                             writes = {}
	                                             local texts = { [256] = 'a\0b\0c', [512] = '\255\254A' }
	                                             local function record(kind, maximumLength, wide)
	                                               calls[#calls + 1] = kind .. '|' .. math.type(maximumLength) .. '|' .. tostring(maximumLength) .. '|' .. tostring(wide)
	                                             end
	                                             function readString(a, maximumLength, wide) record('target', maximumLength, wide) return texts[a] end
	                                             function readStringLocal(a, maximumLength, wide) record('host', maximumLength, wide) return texts[a] end
	                                             function writeString(a, v, wide) writes[#writes + 1] = 'target|' .. #v .. '|' .. tostring(wide) return true end
	                                             function writeStringLocal(a, v, wide) writes[#writes + 1] = 'host|' .. #v .. '|' .. tostring(wide) return true end
	                                             function readBytes(a, count, asTable) return { 7, 8 } end
	                                             function readBytesLocal(a, count, asTable) return { 9 } end
	                                             """u8;

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Target_utf8_read_keeps_embedded_nul_and_reports_the_exact_byte_count()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);
		Span<byte> destination = stackalloc byte[16];

		Assert.True(TargetMemory.TryReadUtf8(256UL, 100, destination, false, out int written, out int required,
			out MemoryAccessFailure failure));
		Assert.Equal(MemoryAccessFailure.None, failure);
		Assert.Equal(5, written);
		Assert.Equal(5, required);
		Assert.True(destination[..written].SequenceEqual("a\0b\0c"u8));
		Assert.True(TargetMemory.TryReadString(256UL, 100, false, out string? text, out failure));
		Assert.Equal("a\0b\0c", text);
		Assert.True(HostMemory.TryReadUtf8(new HostAddress(256), 100, destination, false, out written, out failure));
		Assert.Equal(5, written);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Host_and_target_text_reads_pass_maximum_length_and_wide_flag_unchanged()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);
		Span<byte> destination = stackalloc byte[16];

		Assert.True(TargetMemory.TryReadString(256UL, 7, true, out _, out _));
		Assert.True(TargetMemory.TryReadUtf8(256UL, 0, destination, false, out _, out _));
		Assert.True(HostMemory.TryReadString(new HostAddress(256), 3, true, out _, out _));
		Assert.True(HostMemory.TryReadUtf8(new HostAddress(256), int.MaxValue, destination, false, out _, out _));

		// The count is forwarded as an integer, never rescaled for a wide read: its unit is CE's to define.
		EngineTest.Run(scope.State, "return table.concat(calls, ';')"u8, 1);
		Assert.Equal("target|integer|7|true;target|integer|0|false;host|integer|3|true;host|integer|2147483647|false",
			EngineTest.ReadString(scope.State, -1));
		scope.State.Pop(1);
		Assert.Throws<ArgumentOutOfRangeException>(() => TargetMemory.TryReadString(256UL, -1, false, out _, out _));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Utf8_write_with_embedded_nul_reaches_lua_with_its_exact_length()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);

		Assert.True(TargetMemory.TryWriteUtf8(768UL, "x\0y"u8, false, out MemoryAccessFailure failure));
		Assert.Equal(MemoryAccessFailure.None, failure);
		Assert.True(TargetMemory.TryWriteString(768UL, "x\0y\u00E9".AsSpan(), true, out failure));
		Assert.True(HostMemory.TryWriteUtf8(new HostAddress(768), "\0"u8, false, out failure));
		Assert.True(HostMemory.TryWriteString(new HostAddress(768), "\uD800".AsSpan(), false, out failure));

		EngineTest.Run(scope.State, "return table.concat(writes, ';')"u8, 1);
		Assert.Equal("target|3|false;target|5|true;host|1|false;host|3|false", EngineTest.ReadString(scope.State, -1));
		scope.State.Pop(1);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Invalid_utf8_text_read_keeps_raw_bytes_in_the_byte_form()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);
		Span<byte> destination = stackalloc byte[8];

		Assert.True(TargetMemory.TryReadUtf8(512UL, 100, destination, false, out int written,
			out MemoryAccessFailure failure));
		Assert.Equal(MemoryAccessFailure.None, failure);
		Assert.True(destination[..written].SequenceEqual(new byte[] { 0xFF, 0xFE, (byte) 'A' }));

		// The string form is the explicit, lossy conversion: each invalid byte becomes U+FFFD.
		Assert.True(TargetMemory.TryReadString(512UL, 100, false, out string? text, out failure));
		Assert.Equal("\uFFFD\uFFFDA", text);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Partial_byte_read_reports_the_confirmed_prefix_not_zero()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);
		Span<byte> destination = stackalloc byte[4];
		destination.Fill(0xA5);

		Assert.False(TargetMemory.TryReadBytes(16UL, destination, out int written, out MemoryAccessFailure failure));
		Assert.Equal(MemoryAccessFailure.PartialRead, failure);
		Assert.Equal(2, written);
		Assert.True(destination.SequenceEqual(new byte[] { 7, 8, 0xA5, 0xA5 }));

		destination.Fill(0xA5);
		Assert.False(HostMemory.TryReadBytes(new HostAddress(16), destination, out written, out failure));
		Assert.Equal(MemoryAccessFailure.PartialRead, failure);
		Assert.Equal(1, written);
		Assert.True(destination.SequenceEqual(new byte[] { 9, 0xA5, 0xA5, 0xA5 }));

		// The all-or-nothing overload copies nothing and reports the read as failed.
		destination.Fill(0xA5);
		Assert.False(TargetMemory.TryReadBytes(16UL, destination, out failure));
		Assert.Equal(MemoryAccessFailure.ReadFailed, failure);
		Assert.True(destination.SequenceEqual(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5 }));
		Assert.Equal(0, scope.State.Top);
	}
}
