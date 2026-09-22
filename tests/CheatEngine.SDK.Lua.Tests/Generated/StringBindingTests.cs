using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Generated;

/// <summary>
///     The string-result worked example runs: <see cref="StringBindings" /> against a stand-in <c>readString</c>, on every
///     path, and the copied bytes stay valid after the Lua string that held them has been collected and its memory reused.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class StringBindingTests
{
	private static ReadOnlySpan<byte> StandIn => """
	                                             local memory = { [0x1000] = 'Cheat Engine', [0x2000] = string.rep('A', 4096) }
	                                             function readString(address, maxLength)
	                                               if address == 0xDEAD then error('access violation') end
	                                               local s = memory[address]
	                                               if s == nil then return nil end
	                                               return s:sub(1, maxLength)
	                                             end
	                                             function churn()
	                                               collectgarbage(); collectgarbage()
	                                               local keep = {}
	                                               for i = 1, 64 do keep[i] = string.rep('B', 4096) end
	                                               return #keep
	                                             end
	                                             """u8;

	[Fact]
	public void Copy_out_shape_reads_the_bytes_and_leaves_the_stack_balanced()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIn);
		Span<byte> buffer = stackalloc byte[64];

		Assert.True(StringBindings.TryReadString(0x1000, 64, buffer, out int written));

		Assert.Equal(12, written);
		Assert.True(buffer[..written].SequenceEqual("Cheat Engine"u8));
		Assert.Equal(0, L.Top);

		Assert.True(StringBindings.TryReadString(0x1000, 5, buffer, out written));
		Assert.True(buffer[..written].SequenceEqual("Cheat"u8));
	}

	[Fact]
	public void Copy_out_shape_fails_cleanly_for_nil_a_too_small_buffer_a_raise_and_a_missing_global()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Span<byte> buffer = stackalloc byte[8];

		Assert.False(StringBindings.TryReadString(0x1000, 64, buffer, out int written)); // no global yet
		Assert.Equal(0, written);
		LuaTest.Run(L, StandIn);
		Assert.False(StringBindings.TryReadString(0x3000, 64, buffer, out written)); // nil
		Assert.Equal(0, written);
		Assert.False(StringBindings.TryReadString(0x1000, 64, buffer, out written)); // 12 bytes into 8
		Assert.Equal(0, written);
		Assert.False(StringBindings.TryReadString(0xDEAD, 64, buffer, out written)); // raised
		Assert.Equal(0, written);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Copied_bytes_survive_collection_and_reuse_of_the_lua_string_memory()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIn);
		byte[] copy = new byte[4096];

		// The result string is 4096 x 'A'; after the wrapper returned it is unreachable from Lua. A span into it
		// would now be dangling (the churn below collects garbage and allocates 'B' strings, which can reuse that
		// memory); the copy-out shape has already taken the bytes.
		Assert.True(StringBindings.TryReadString(0x2000, 4096, copy, out int written));
		Assert.Equal(4096, written);
		LuaTest.Run(L, "return churn()"u8, 1);
		Assert.True(L.TryReadInteger(-1, out long kept));
		Assert.Equal(64, kept);
		L.Pop(1);

		Assert.True(copy.AsSpan().IndexOfAnyExcept((byte) 'A') < 0);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void String_shape_decodes_the_text_and_is_null_on_failure()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIn);

		Assert.True(StringBindings.TryReadString(0x1000, 64, out string? text));
		Assert.Equal("Cheat Engine", text);
		Assert.False(StringBindings.TryReadString(0x3000, 64, out string? missing));
		Assert.Null(missing);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Copy_out_shape_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIn);
		byte[] buffer = new byte[64];
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			if (!StringBindings.TryReadString(0x1000, 64, buffer, out int written) || written != 12)
			{
				throw new InvalidOperationException("wrong value");
			}

			sink += written;
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}
}
