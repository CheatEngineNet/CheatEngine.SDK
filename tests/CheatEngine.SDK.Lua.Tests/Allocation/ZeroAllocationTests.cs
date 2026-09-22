using System.Text;

using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Callbacks;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Lua.Text;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Allocation;

/// <summary>
///     The merge gate: push and read of every scalar, a protected global call and a callback round trip allocate
///     nothing on the managed side once warm.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class ZeroAllocationTests
{
	private static readonly LuaRef s_add = new();

	[Fact]
	public void Push_and_read_of_each_scalar_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		long sink = 0;

		AllocationGate.AssertZero(() => sink += PushAndReadEachScalar(L));

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	private static long PushAndReadEachScalar(LuaState state)
	{
		int top = state.Top;
		PushEachScalar(state);
		long result = ReadEachScalar(state, top);
		state.SetTop(top);
		return result;
	}

	private static void PushEachScalar(LuaState state)
	{
		Int32Marshaller.Push(state, 42);
		Int64Marshaller.Push(state, long.MinValue);
		DoubleMarshaller.Push(state, 2.5);
		SingleMarshaller.Push(state, 1.5f);
		BooleanMarshaller.Push(state, true);
		AddressMarshaller.Push(state, unchecked((nuint) 0xFFFF_FFFF_FFFF_FFF0UL));
		Utf8Marshaller.Push(state, "text"u8);
		state.PushNil();
		state.PushLightUserdata(0x10);
	}

	private static long ReadEachScalar(LuaState state, int top)
	{
		if (!Int32Marshaller.TryRead(state, top + 1, out int i) || i != 42)
		{
			Fail();
		}

		if (!Int64Marshaller.TryRead(state, top + 2, out long l) || l != long.MinValue)
		{
			Fail();
		}

		// Exact on purpose: the round trip through the Lua stack must not change a single bit,
		// so the bit patterns are compared.
		if (!DoubleMarshaller.TryRead(state, top + 3, out double d) ||
		    BitConverter.DoubleToInt64Bits(d) != BitConverter.DoubleToInt64Bits(2.5))
		{
			Fail();
		}

		if (!SingleMarshaller.TryRead(state, top + 4, out float f) ||
		    BitConverter.SingleToInt32Bits(f) != BitConverter.SingleToInt32Bits(1.5f))
		{
			Fail();
		}

		if (!BooleanMarshaller.TryRead(state, top + 5, out bool b) || !b)
		{
			Fail();
		}

		if (!AddressMarshaller.TryRead(state, top + 6, out UIntPtr a) ||
		    a != unchecked((nuint) 0xFFFF_FFFF_FFFF_FFF0UL))
		{
			Fail();
		}

		if (!Utf8Marshaller.TryRead(state, top + 7, out ReadOnlySpan<byte> s) || !s.SequenceEqual("text"u8))
		{
			Fail();
		}

		if (state.TypeOf(top + 8) != LuaType.Nil || state.ToUserdata(top + 9) != 0x10)
		{
			Fail();
		}

		return i + l;
	}

	[Fact]
	public void Non_ascii_utf8_payload_at_the_benchmark_upper_bound_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		byte[] payload = new byte[1024];

		for (int index = 0; index < payload.Length; index += 4)
		{
			payload[index] = 0xF0;
			payload[index + 1] = 0x9F;
			payload[index + 2] = 0xA7;
			payload[index + 3] = 0xAA;
		}

		long sink = 0;
		AllocationGate.AssertZero(() =>
		{
			int top = L.Top;
			Utf8Marshaller.Push(L, payload);
			if (!Utf8Marshaller.TryRead(L, -1, out ReadOnlySpan<byte> read) || !read.SequenceEqual(payload))
			{
				Fail();
			}

			sink += read.Length;
			L.SetTop(top);
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Utf16_string_push_allocates_nothing_through_the_stack_buffer()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		// 171 characters: the encoder's worst case (3 bytes each, plus one) exceeds the 512-byte buffer, so Encode
		// takes its exact-count branch; with 11 two-byte characters the text is 182 bytes and still fits.
		string text = new string('a', 160) + new string('\u00E9', 11);
		byte[] expected = Encoding.UTF8.GetBytes(text);
		Assert.Equal(171, text.Length);
		Assert.True(Encoding.UTF8.GetMaxByteCount(text.Length) > Utf8Scratch.StackBufferSize);
		Assert.True(expected.Length <= Utf8Scratch.StackBufferSize);
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			int top = L.Top;
			StringMarshaller.Push(L, text);
			if (!Utf8Marshaller.TryRead(L, -1, out ReadOnlySpan<byte> read) || !read.SequenceEqual(expected))
			{
				Fail();
			}

			sink += read.Length;
			L.SetTop(top);
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Utf16_string_push_above_the_stack_buffer_allocates_nothing_once_the_pool_is_warm()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		string text = new('\u20AC', 200); // 600 bytes: rented from the pool and returned after the push
		byte[] expected = Encoding.UTF8.GetBytes(text);
		Assert.True(expected.Length > Utf8Scratch.StackBufferSize);
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			int top = L.Top;
			L.PushString(text);
			if (!Utf8Marshaller.TryRead(L, -1, out ReadOnlySpan<byte> read) || !read.SequenceEqual(expected))
			{
				Fail();
			}

			sink += read.Length;
			L.SetTop(top);
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Protected_global_call_with_two_arguments_and_one_result_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, "function add(a, b) return a + b end"u8);
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			int top = L.Top;
			if (!LuaGlobalFunctions.TryPush(L, s_add, "add"u8))
			{
				Fail();
			}

			Int64Marshaller.Push(L, 40);
			Int64Marshaller.Push(L, 2);
			if (!L.TryCall(2, 1).IsOk)
			{
				Fail();
			}

			if (!Int64Marshaller.TryRead(L, -1, out long result) || result != 42)
			{
				Fail();
			}

			sink += result;
			L.SetTop(top);
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Protected_call_of_a_function_on_the_stack_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, "return function(a, b) return a * b end"u8, 1);
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			L.PushValue(1);
			Int64Marshaller.Push(L, 6);
			Int64Marshaller.Push(L, 7);
			if (!L.TryCall(2, 1).IsOk)
			{
				Fail();
			}

			if (!Int64Marshaller.TryRead(L, -1, out long result) || result != 42)
			{
				Fail();
			}

			sink += result;
			L.Pop(1);
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(1, L.Top);
	}

	[Fact]
	public void Protected_field_access_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, "return setmetatable({}, { __index = function(_, k) return #k end })"u8, 1);
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			if (!L.TryGetField(1, "abcd"u8).IsOk)
			{
				Fail();
			}

			if (!Int64Marshaller.TryRead(L, -1, out long result) || result != 4)
			{
				Fail();
			}

			sink += result;
			L.Pop(1);
		});

		Assert.NotEqual(0, sink);
	}

	[Fact]
	public void Callback_round_trip_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.True(callback!.TryRegister(L, "count"u8).IsOk);
		Assert.True(L.TryPushFunction(Thunks.Add).IsOk);
		Assert.True(L.TrySetGlobal("add"u8).IsOk);
		// The loop lives in Lua: one protected call runs many callbacks, which is the legitimate use of a callback.
		LuaTest.Run(L, "return function(n) local s = 0 for i = 1, n do s = s + add(i, count()) end return s end"u8, 1);
		long sink = 0;

		AllocationGate.AssertZero(
			() =>
			{
				L.PushValue(1);
				Int64Marshaller.Push(L, 100);
				if (!L.TryCall(1, 1).IsOk)
				{
					Fail();
				}

				if (!Int64Marshaller.TryRead(L, -1, out long result))
				{
					Fail();
				}

				sink += result;
				L.Pop(1);
			},
			200);

		Assert.True(counter.Value > 200 * 100);
		Assert.NotEqual(0, sink);
		callback.Release(L);
	}

	[Fact]
	public void Failure_path_of_a_protected_call_allocates_nothing_until_the_error_is_read()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, "return function() error('expected') end"u8, 1);
		int failures = 0;

		AllocationGate.AssertZero(() =>
		{
			int top = L.Top;
			L.PushValue(1);
			LuaStatus status = L.TryCall(0, 0);
			if (status.IsOk)
			{
				Fail();
			}

			failures += LuaCallSupport.Fail(L, top) ? 0 : 1;
		});

		Assert.True(failures > 0);
		Assert.Equal(1, L.Top);
	}

	private static void Fail()
	{
		throw new InvalidOperationException("The round trip produced a wrong value.");
	}
}
