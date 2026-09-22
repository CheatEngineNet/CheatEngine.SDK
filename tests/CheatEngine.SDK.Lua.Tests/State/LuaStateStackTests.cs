using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Callbacks;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.State;

/// <summary>Stack manipulation, type tests and raw table access of the state view.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaStateStackTests
{
	[Fact]
	public void Push_and_type_tests_agree()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		L.PushNil();
		L.PushBoolean(true);
		L.PushLightUserdata(0x1234);
		L.PushInteger(7);
		L.PushNumber(7.5);
		L.PushString("s"u8);
		L.CreateTable();
		L.PushGlobalTable();

		Assert.Equal(8, L.Top);
		Assert.Equal(LuaType.Nil, L.TypeOf(1));
		Assert.Equal(LuaType.Boolean, L.TypeOf(2));
		Assert.Equal(LuaType.LightUserdata, L.TypeOf(3));
		Assert.Equal(LuaType.Number, L.TypeOf(4));
		Assert.Equal(LuaType.Number, L.TypeOf(5));
		Assert.Equal(LuaType.String, L.TypeOf(6));
		Assert.Equal(LuaType.Table, L.TypeOf(7));
		Assert.Equal(LuaType.Table, L.TypeOf(8));
		Assert.Equal(LuaType.None, L.TypeOf(9));

		Assert.True(L.IsNil(1));
		Assert.True(L.IsNoneOrNil(1));
		Assert.True(L.IsNoneOrNil(9));
		Assert.True(L.IsNone(9));
		Assert.False(L.IsNone(8));
		Assert.True(L.IsLightUserdata(3));
		Assert.False(L.IsUserdata(3));
		Assert.True(L.IsInteger(4));
		Assert.False(L.IsInteger(5));
		Assert.True(L.IsNumberConvertible(5));
		Assert.False(L.IsNumberConvertible(6));
		Assert.True(L.IsTable(7));
		Assert.False(L.IsFunction(7));
		Assert.Equal(0x1234, L.ToUserdata(3));
		Assert.Equal(0, L.ToUserdata(4));
		Assert.NotEqual(0, L.ToPointer(7));
		Assert.True(L.ToBoolean(2));
		Assert.False(L.ToBoolean(1));
		Assert.True(L.ToBoolean(4));
	}

	[Fact]
	public void TypeName_gives_lua_own_names_without_allocating()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushNil();
		L.PushBoolean(false);
		L.PushLightUserdata(0);
		L.PushInteger(1);
		L.PushString("s"u8);
		L.CreateTable();
		L.PushUncheckedFunction(Thunks.Add);
		L.NewUserdata(1);

		Assert.True(L.TypeName(1).SequenceEqual("nil"u8));
		Assert.True(L.TypeName(2).SequenceEqual("boolean"u8));
		Assert.True(L.TypeName(3).SequenceEqual("userdata"u8));
		Assert.True(L.TypeName(4).SequenceEqual("number"u8));
		Assert.True(L.TypeName(5).SequenceEqual("string"u8));
		Assert.True(L.TypeName(6).SequenceEqual("table"u8));
		Assert.True(L.TypeName(7).SequenceEqual("function"u8));
		Assert.True(L.TypeName(8).SequenceEqual("userdata"u8));
		Assert.True(L.TypeName(9).SequenceEqual("no value"u8));
		Assert.True(L.TypeName(LuaType.Thread).SequenceEqual("thread"u8));
		Assert.True(L.TypeName(LuaType.None).SequenceEqual("no value"u8));
		Assert.Equal(8, L.Top);

		long sink = 0;
		AllocationGate.AssertZero(() => sink += L.TypeName(4).Length + L.TypeName(LuaType.Nil).Length);
		Assert.NotEqual(0, sink);
	}

	[Fact]
	public void Insert_remove_replace_copy_rotate_and_absolute_index()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		for (int i = 1; i <= 4; i++)
		{
			L.PushInteger(i);
		}

		// [1 2 3 4] -> insert top at 1 -> [4 1 2 3]
		L.Insert(1);
		Assert.Equal(4, Read(L, 1));
		Assert.Equal(3, Read(L, 4));

		// remove index 2 (the 1) -> [4 2 3]
		L.Remove(2);
		Assert.Equal(3, L.Top);
		Assert.Equal(2, Read(L, 2));

		// push 9, replace index 1 -> [9 2 3]
		L.PushInteger(9);
		L.Replace(1);
		Assert.Equal(3, L.Top);
		Assert.Equal(9, Read(L, 1));

		// copy 1 -> 3 : [9 2 9]
		L.Copy(1, 3);
		Assert.Equal(9, Read(L, 3));

		// rotate whole stack by 1 : [9 9 2]
		L.Rotate(1, 1);
		Assert.Equal(9, Read(L, 1));
		Assert.Equal(2, Read(L, 3));

		Assert.Equal(3, L.AbsoluteIndex(-1));
		Assert.Equal(1, L.AbsoluteIndex(-3));
		Assert.Equal(LuaState.RegistryIndex, L.AbsoluteIndex(LuaState.RegistryIndex));

		L.PushValue(1);
		Assert.Equal(4, L.Top);
		Assert.Equal(9, Read(L, 4));
		L.Pop(4);
		Assert.Equal(0, L.Top);
		L.SetTop(2);
		Assert.Equal(2, L.Top);
		Assert.True(L.IsNil(1));
	}

	[Fact]
	public void EnsureStack_grows_within_limits_and_refuses_absurd_requests()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		Assert.True(L.TryEnsureStack(10_000));
		Assert.False(L.TryEnsureStack(int.MaxValue));
	}

	[Fact]
	public void PushUncheckedFunction_reserves_the_light_C_function_slot_before_each_push()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		int initialTop = L.Top;
		const int functionCount = 64;

		// CE's pinned lapi.c makes lua_pushcclosure(..., 0) a light C function: it only writes one already-reserved
		// stack slot. Do not reserve here: every production call must make the immediate lua_checkstack(L, 1) reservation
		// itself before it takes the direct fast path. Crossing the initial free-slot boundary proves the method retains
		// that precondition instead of relying on a caller's incidental reservation.
		for (int i = 0; i < functionCount; i++)
		{
			L.PushUncheckedFunction(Thunks.Add);
			Assert.Equal(initialTop + i + 1, L.Top);
			Assert.Equal(LuaType.Function, L.TypeOf(-1));
		}

		L.SetTop(initialTop);
		Assert.Equal(initialTop, L.Top);
	}

	[Fact]
	public void Raw_table_access_bypasses_metamethods()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, "return setmetatable({ real = 1, 10, 20 }, { __index = function() error('never') end })"u8, 1);

		Assert.Equal(LuaType.Number, L.RawGetIndex(1, 2));
		Assert.Equal(20, Read(L, -1));
		L.Pop(1);

		L.PushString("real"u8);
		Assert.Equal(LuaType.Number, L.RawGet(1));
		Assert.Equal(1, Read(L, -1));
		L.Pop(1);

		L.PushString("missing"u8);
		Assert.Equal(LuaType.Nil, L.RawGet(1));
		L.Pop(1);

		L.PushInteger(30);
		L.RawSetIndex(1, 3);
		Assert.Equal((nuint) 3, L.RawLength(1));

		L.PushString("key"u8);
		L.PushBoolean(true);
		Assert.True(L.TryRawSet(1));
		L.PushString("key"u8);
		Assert.Equal(LuaType.Boolean, L.RawGet(1));
		L.Pop(1);

		L.PushInteger(99);
		L.RawSetPointer(1, 0x77);
		Assert.Equal(LuaType.Number, L.RawGetPointer(1, 0x77));
		Assert.Equal(LuaType.Nil, L.RawGetPointer(1, 0x78));
		L.Pop(2);

		Assert.True(L.TryGetMetatable(1));
		Assert.True(L.IsTable(-1));
		L.Pop(1);
		L.PushNil();
		L.SetMetatable(1);
		Assert.False(L.TryGetMetatable(1));
		Assert.Equal(1, L.Top);
	}

	[Fact]
	public void PushByteTable_creates_an_ordered_one_based_byte_sequence_in_one_stack_value()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		ReadOnlySpan<byte> bytes = [0, 1, 127, byte.MaxValue];

		L.PushByteTable(bytes);

		Assert.Equal(1, L.Top);
		Assert.True(L.IsTable(-1));
		Assert.Equal((nuint) bytes.Length, L.RawLength(-1));
		for (int index = 0; index < bytes.Length; index++)
		{
			Assert.Equal(LuaType.Number, L.RawGetIndex(-1, index + 1L));
			Assert.True(L.TryReadInteger(-1, out long value));
			Assert.Equal(bytes[index], value);
			L.Pop(1);
		}

		L.Pop(1);
		L.PushByteTable([]);
		Assert.Equal(1, L.Top);
		Assert.True(L.IsTable(-1));
		Assert.Equal((nuint) 0, L.RawLength(-1));
	}

	[Fact]
	public void TryNext_walks_a_table_and_raw_equality_is_primitive()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(); // TryNext uses the base library's next, like TryToString uses tostring
		LuaState L = LuaTest.View(state);
		L.CreateTable(3);
		for (int i = 1; i <= 3; i++)
		{
			L.PushInteger(i * 10);
			L.RawSetIndex(1, i);
		}

		long sum = 0;
		int count = 0;
		L.PushNil();
		while (true)
		{
			Assert.True(L.TryNext(1, out bool hasNext).IsOk);
			if (!hasNext)
			{
				break;
			}

			Assert.True(L.TryReadInteger(-1, out long value));
			sum += value;
			count++;
			L.Pop(1);
		}

		Assert.Equal(3, count);
		Assert.Equal(60, sum);
		Assert.Equal(1, L.Top);

		L.PushInteger(5);
		L.PushInteger(5);
		L.PushNumber(5.0);
		Assert.True(L.RawEquals(2, 3));
		Assert.True(L.RawEquals(2, 4));
		Assert.False(L.RawEquals(1, 2));
	}

	[Fact]
	public void TryNext_with_a_key_that_is_not_in_the_table_is_a_runtime_error_not_a_raise()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, "return { a = 1 }"u8, 1);

		// lua_next raises "invalid key to 'next'" for a key the table never had; the protected form reports it.
		L.PushString("never"u8);
		LuaStatus status = L.TryNext(1, out bool hasNext);

		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.False(hasNext);
		Assert.Equal(2, L.Top);
		Assert.Contains("next", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		// A key that is in the table, given as a relative index below the key.
		L.PushString("a"u8);
		Assert.True(L.TryNext(-2, out hasNext).IsOk);
		Assert.False(hasNext);
		Assert.Equal(1, L.Top);
	}

	[Fact]
	public void TryRawSet_refuses_nil_and_nan_keys_instead_of_letting_lua_raise()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.CreateTable();

		L.PushNil();
		L.PushInteger(1);
		Assert.False(L.TryRawSet(1));
		Assert.Equal(1, L.Top);

		L.PushNumber(double.NaN);
		L.PushInteger(1);
		Assert.False(L.TryRawSet(1));
		Assert.Equal(1, L.Top);

		// A float key with an integral value is normalised by Lua and is fine; so is any other key.
		L.PushNumber(2.0);
		L.PushInteger(20);
		Assert.True(L.TryRawSet(1));
		L.PushNumber(2.5);
		L.PushInteger(25);
		Assert.True(L.TryRawSet(1));
		L.PushBoolean(true);
		L.PushInteger(1);
		Assert.True(L.TryRawSet(1));

		Assert.Equal(LuaType.Number, L.RawGetIndex(1, 2));
		Assert.True(L.TryReadInteger(-1, out long two));
		Assert.Equal(20, two);
		L.PushNumber(2.5);
		Assert.Equal(LuaType.Number, L.RawGet(1));
		L.PushBoolean(true);
		Assert.Equal(LuaType.Number, L.RawGet(1));
		L.PushNil();
		Assert.Equal(LuaType.Nil, L.RawGet(1));
		Assert.Equal(5, L.Top);
	}

	[Fact]
	public void NewUserdata_gives_a_stable_block_owned_by_lua()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		IntPtr block = L.NewUserdata(16);

		Assert.NotEqual(0, block);
		Assert.True(L.IsUserdata(-1));
		Assert.Equal(block, L.ToUserdata(-1));
		Assert.Equal((nuint) 16, L.RawLength(-1));
	}

	private static long Read(LuaState L, int index)
	{
		Assert.True(L.TryReadInteger(index, out long value));
		return value;
	}
}
