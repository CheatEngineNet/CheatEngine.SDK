using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class CallTests
{
	[Fact]
	public void Pcallk_success_replaces_function_and_arguments_with_results()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;
		LuaTest.Run(L, "return function(a, b) return a + b, a * b end"u8, 1);

		lua_pushinteger(L, 6);
		lua_pushinteger(L, 7);
		int status = lua_pcallk(L, 2, 2, 0, 0, null);

		Assert.Equal(LUA_OK, status);
		Assert.Equal(2, lua_gettop(L));
		Assert.Equal(13, lua_tointeger(L, 1));
		Assert.Equal(42, lua_tointeger(L, 2));
	}

	[Fact]
	public void Pcallk_multret_keeps_every_result()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		Assert.Equal(LUA_OK, LuaTest.Load(L, "return 1, 2, 3, 4, 5"u8));
		Assert.Equal(LUA_OK, lua_pcallk(L, 0, LUA_MULTRET, 0, 0, null));

		Assert.Equal(5, lua_gettop(L));
	}

	[Fact]
	public void Pcallk_runtime_error_returns_errrun_and_exactly_the_message()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		lua_State* L = state.L;

		Assert.Equal(LUA_OK, LuaTest.Load(L, "local depth = ...\nerror('boom from lua')"u8));
		lua_pushinteger(L, 1);
		int status = lua_pcallk(L, 1, 3, 0, 0, null);

		Assert.Equal(LUA_ERRRUN, status);
		Assert.Equal(1, lua_gettop(L));
		Assert.Equal("test:2: boom from lua", LuaTest.ReadString(L, -1));
	}

	[Fact]
	public void Pcallk_error_value_can_be_any_lua_value()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		lua_State* L = state.L;

		Assert.Equal(LUA_OK, LuaTest.Load(L, "error({ code = 17 })"u8));
		int status = lua_pcall(L, 0, 0, 0);

		Assert.Equal(LUA_ERRRUN, status);
		Assert.True(lua_istable(L, -1));
		fixed (byte* code = "code"u8)
		{
			Assert.Equal(LUA_TNUMBER, lua_getfield(L, -1, code));
			Assert.Equal(17, lua_tointeger(L, -1));
		}
	}

	[Fact]
	public void Pcallk_message_handler_index_is_the_fourth_argument()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		lua_State* L = state.L;
		LuaTest.Run(L, "return function(message) return 'handled: ' .. message end"u8, 1);
		int handler = lua_gettop(L);

		Assert.Equal(LUA_OK, LuaTest.Load(L, "local t = nil\nreturn t.field"u8));
		int status = lua_pcallk(L, 0, 0, handler, 0, null);

		Assert.Equal(LUA_ERRRUN, status);
		Assert.StartsWith("handled: test:2:", LuaTest.ReadString(L, -1), StringComparison.Ordinal);
		Assert.Equal(handler + 1, lua_gettop(L));
	}

	[Fact]
	public void Pcallk_reports_a_failing_finalizer_as_errgcmm_although_no_allocation_failed()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		lua_State* L = state.L;

		// What "Raises: memory" means in Lua 5.3: the loop allocates only through string.rep, that is through
		// lua_pushlstring called by the native string library. One of those calls runs the collector step that
		// finalizes the unreachable table, and the failing __gc unwinds from there to the protected call.
		Assert.Equal(LUA_OK,
			LuaTest.Load(L,
				"setmetatable({}, { __gc = function() error('finalizer failed') end })\nfor i = 1, 1000000 do local s = string.rep('x', 64) end\nreturn 'finished'"u8));
		int status = lua_pcallk(L, 0, 1, 0, 0, null);

		Assert.Equal(LUA_ERRGCMM, status);
		Assert.Equal(1, lua_gettop(L));
		Assert.Contains("finalizer failed", LuaTest.ReadString(L, -1), StringComparison.Ordinal);
	}

	[Fact]
	public void Load_reports_syntax_errors_without_running_anything()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		int status = LuaTest.Load(L, "return +"u8);

		Assert.Equal(LUA_ERRSYNTAX, status);
		Assert.Equal(1, lua_gettop(L));
		Assert.StartsWith("test:1:", LuaTest.ReadString(L, -1), StringComparison.Ordinal);
	}

	[Fact]
	public void Loadbufferx_text_mode_refuses_a_binary_chunk()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);

		int status = LuaTest.Load(state.L, LUA_SIGNATURE);

		Assert.Equal(LUA_ERRSYNTAX, status);
	}

	[Fact]
	public void Loadstring_and_dostring_take_nul_terminated_source()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		fixed (byte* good = "return 40 + 2"u8)
		fixed (byte* bad = "return nil + 1"u8)
		{
			Assert.Equal(LUA_OK, luaL_loadstring(L, good));
			lua_call(L, 0, 1);
			Assert.Equal(42, lua_tointeger(L, -1));

			Assert.Equal(0, luaL_dostring(L, good));
			Assert.Equal(42, lua_tointeger(L, -1));
			Assert.Equal(1, luaL_dostring(L, bad));
			Assert.Equal(LUA_TSTRING, lua_type(L, -1));
		}
	}

	[Fact]
	public void Loadfilex_missing_file_returns_errfile_with_a_message()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		fixed (byte* path = "cheatengine-sdk-no-such-directory/no-such-file.lua"u8)
		{
			Assert.Equal(LUA_ERRFILE, luaL_loadfilex(L, path, null));
			Assert.Contains("no-such-file.lua", LuaTest.ReadString(L, -1), StringComparison.Ordinal);
			Assert.Equal(LUA_ERRFILE, luaL_loadfile(L, path));
			Assert.Equal(1, luaL_dofile(L, path));
		}
	}

	[Fact]
	public void Load_pulls_the_chunk_through_a_managed_reader()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		fixed (byte* first = "return 6 "u8)
		fixed (byte* second = "* 7"u8)
		fixed (byte* name = "=pieces"u8)
		{
			ReaderState pieces = new()
			{
				First = first,
				FirstSize = 9,
				Second = second,
				SecondSize = 3
			};

			Assert.Equal(LUA_OK, lua_load(L, &ReadPieces, &pieces, name, null));
			Assert.Equal(3, pieces.Calls);
		}

		Assert.Equal(LUA_OK, lua_pcall(L, 0, 1, 0));
		Assert.Equal(42, lua_tointeger(L, -1));
	}

	[Fact]
	public void Dump_streams_a_binary_chunk_that_loads_back()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;
		Assert.Equal(LUA_OK, LuaTest.Load(L, "return 'dumped'"u8));

		WriterState written = default;
		Assert.Equal(0, lua_dump(L, &AppendPiece, &written, 1));

		Assert.False(written.Overflowed);
		Assert.True(new ReadOnlySpan<byte>(written.Bytes, 4).SequenceEqual(LUA_SIGNATURE));
		fixed (byte* name = "=dump"u8)
		fixed (byte* binaryOnly = "b"u8)
		{
			Assert.Equal(LUA_OK, luaL_loadbufferx(L, written.Bytes, written.Length, name, binaryOnly));
		}

		Assert.Equal(LUA_OK, lua_pcall(L, 0, 1, 0));
		Assert.Equal("dumped", LuaTest.ReadString(L, -1));
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static byte* ReadPieces(lua_State* L, void* ud, nuint* size)
	{
		ReaderState* pieces = (ReaderState*) ud;
		pieces->Calls++;
		switch (pieces->Calls)
		{
			case 1:
				*size = pieces->FirstSize;
				return pieces->First;
			case 2:
				*size = pieces->SecondSize;
				return pieces->Second;
			default:
				*size = 0;
				return null;
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int AppendPiece(lua_State* L, void* p, nuint sz, void* ud)
	{
		WriterState* written = (WriterState*) ud;
		if (sz > WriterState.Capacity - written->Length)
		{
			written->Overflowed = true;
			return 1;
		}

		Buffer.MemoryCopy(p, written->Bytes + written->Length, WriterState.Capacity - written->Length, sz);
		written->Length += sz;
		return 0;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ReaderState
	{
		public byte* First;
		public nuint FirstSize;
		public byte* Second;
		public nuint SecondSize;
		public int Calls;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WriterState
	{
		public const int Capacity = 4096;

		[SuppressMessage("Meziantou.Analyzer", "MA0189",
			Justification =
				"This fixed buffer is embedded in native callback state and its unmanaged layout is intentional.")]
		public fixed byte Bytes[Capacity];

		public nuint Length;
		public bool Overflowed;
	}
}
