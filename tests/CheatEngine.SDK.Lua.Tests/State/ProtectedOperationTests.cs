using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.State;

/// <summary>
///     Every operation that can run a metamethod goes through <c>lua_pcallk</c>: a metamethod that raises surfaces as a
///     status with the error value on the stack, never as a crash, and the stack is exactly as documented afterwards.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class ProtectedOperationTests
{
	private static ReadOnlySpan<byte> RaisingTable => """
	                                                  local t = setmetatable({}, {
	                                                    __index = function(_, k) error('index raised for ' .. tostring(k)) end,
	                                                    __newindex = function(_, k) error('newindex raised for ' .. tostring(k)) end,
	                                                    __len = function() error('len raised') end,
	                                                    __tostring = function() error('tostring raised') end,
	                                                    __eq = function() error('eq raised') end,
	                                                    __lt = function() error('lt raised') end,
	                                                  })
	                                                  return t
	                                                  """u8;

	[Fact]
	public void Globals_are_read_and_written_under_protection()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		L.PushInteger(42);
		Assert.True(L.TrySetGlobal("answer"u8).IsOk);
		Assert.Equal(0, L.Top);

		Assert.True(L.TryGetGlobal("answer"u8).IsOk);
		Assert.True(L.TryReadInteger(-1, out long value));
		Assert.Equal(42, value);

		Assert.True(L.TryGetGlobal("undefined"u8).IsOk);
		Assert.True(L.IsNil(-1));
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void A_raising_index_metamethod_on_the_globals_table_becomes_a_status()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L,
			"setmetatable(_G, { __index = function(_, k) error('no global ' .. k) end, __newindex = function(_, k) error('read-only ' .. k) end })"u8);

		LuaStatus getStatus = L.TryGetGlobal("missing"u8);
		Assert.Equal(LuaStatus.RuntimeError, getStatus);
		Assert.Equal(1, L.Top);
		Assert.Contains("no global missing", LuaError.FromStack(L, getStatus).Message, StringComparison.Ordinal);
		L.Pop(1);

		L.PushInteger(1);
		LuaStatus setStatus = L.TrySetGlobal("newGlobal"u8);
		Assert.Equal(LuaStatus.RuntimeError, setStatus);
		Assert.Equal(1, L.Top);
		Assert.Contains("read-only newGlobal", LuaError.FromStack(L, setStatus).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Fields_are_read_and_written_through_metamethods_under_protection()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L,
			"return setmetatable({}, { __index = function(_, k) return 'got ' .. k end, __newindex = function(t, k, v) rawset(t, k, v * 2) end })"u8,
			1);

		Assert.True(L.TryGetField(1, "name"u8).IsOk);
		Assert.Equal("got name", LuaTest.ReadString(L, -1));
		L.Pop(1);

		// Relative index, with the value already on top.
		L.PushInteger(21);
		Assert.True(L.TrySetField(-2, "doubled"u8).IsOk);
		Assert.Equal(1, L.Top);
		L.PushString("doubled"u8);
		Assert.Equal(LuaType.Number, L.RawGet(1));
		Assert.True(L.TryReadInteger(-1, out long doubled));
		Assert.Equal(42, doubled);
		L.Pop(1);

		Assert.True(L.TryGetField(-1, "other"u8).IsOk);
		Assert.Equal("got other", LuaTest.ReadString(L, -1));
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void A_table_whose_index_raises_surfaces_as_a_status_not_a_crash()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, RaisingTable, 1);

		LuaStatus status = L.TryGetField(1, "prop"u8);

		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal(2, L.Top);
		Assert.Contains("index raised for prop", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		L.PushInteger(5);
		status = L.TrySetField(1, "prop"u8);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal(2, L.Top);
		Assert.Contains("newindex raised for prop", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		status = L.TryGetIndex(1, 3);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("index raised for 3", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		L.PushBoolean(true);
		status = L.TrySetIndex(1, 3);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("newindex raised for 3", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		L.PushString("k"u8);
		status = L.TryGetTable(1);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal(2, L.Top);
		L.Pop(1);

		L.PushString("k"u8);
		L.PushInteger(1);
		status = L.TrySetTable(1);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void A_table_whose_len_tostring_or_lt_raises_surfaces_as_a_status_not_a_crash()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, RaisingTable, 1);

		LuaStatus status = L.TryLength(1);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("len raised", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		status = L.TryToString(1);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("tostring raised", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		L.PushValue(1);
		status = L.TryCompare(1, 2, LuaComparison.Less, out bool less);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.False(less);
		Assert.Contains("lt raised", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void Table_index_and_integer_access_work_on_plain_tables()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.CreateTable();

		L.PushString("key"u8);
		L.PushInteger(7);
		Assert.True(L.TrySetTable(1).IsOk);
		Assert.Equal(1, L.Top);

		L.PushString("key"u8);
		Assert.True(L.TryGetTable(1).IsOk);
		Assert.True(L.TryReadInteger(-1, out long value));
		Assert.Equal(7, value);
		L.Pop(1);

		L.PushString("first"u8);
		Assert.True(L.TrySetIndex(1, 1).IsOk);
		L.PushString("second"u8);
		Assert.True(L.TrySetIndex(-2, 2).IsOk);
		Assert.True(L.TryGetIndex(1, 2).IsOk);
		Assert.Equal("second", LuaTest.ReadString(L, -1));
		L.Pop(1);

		Assert.True(L.TryLength(1).IsOk);
		Assert.True(L.TryReadInteger(-1, out long length));
		Assert.Equal(2, length);
		L.Pop(1);

		Assert.True(L.TryGetIndex(1, 99).IsOk);
		Assert.True(L.IsNil(-1));
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void Indexing_a_value_that_cannot_be_indexed_is_a_status()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushInteger(5);

		LuaStatus status = L.TryGetField(1, "x"u8);

		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("index", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ToString_length_and_compare_honour_metamethods()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L,
			"local mt = { __tostring = function(t) return 'obj#' .. t.id end, __len = function() return 99 end, __eq = function(a, b) return a.id == b.id end, __lt = function(a, b) return a.id < b.id end, __le = function(a, b) return a.id <= b.id end }\nreturn setmetatable({ id = 1 }, mt), setmetatable({ id = 1 }, mt), setmetatable({ id = 2 }, mt)"u8,
			3);

		Assert.True(L.TryToString(1).IsOk);
		Assert.Equal("obj#1", LuaTest.ReadString(L, -1));
		L.Pop(1);

		Assert.True(L.TryLength(1).IsOk);
		Assert.True(L.TryReadInteger(-1, out long length));
		Assert.Equal(99, length);
		L.Pop(1);

		Assert.True(L.TryCompare(1, 2, LuaComparison.Equal, out bool equal).IsOk);
		Assert.True(equal);
		Assert.True(L.TryCompare(1, 3, LuaComparison.Equal, out equal).IsOk);
		Assert.False(equal);
		Assert.True(L.TryCompare(1, 3, LuaComparison.Less, out bool less).IsOk);
		Assert.True(less);
		Assert.True(L.TryCompare(-1, -3, LuaComparison.Less, out less).IsOk);
		Assert.False(less);
		Assert.True(L.TryCompare(1, 2, LuaComparison.LessOrEqual, out bool lessOrEqual).IsOk);
		Assert.True(lessOrEqual);
		Assert.Equal(3, L.Top);

		Assert.True(L.TryToString(-1).IsOk);
		Assert.Equal("obj#2", LuaTest.ReadString(L, -1));
	}

	[Fact]
	public void Load_reports_syntax_errors_and_execute_runs_chunks()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		LuaStatus syntax = L.TryLoad("return +"u8, "=chunk"u8);
		Assert.Equal(LuaStatus.SyntaxError, syntax);
		Assert.StartsWith("chunk:1:", LuaError.FromStack(L, syntax).Message, StringComparison.Ordinal);
		L.Pop(1);

		Assert.True(L.TryLoad("return 6 * 7"u8).IsOk);
		Assert.True(L.IsFunction(-1));
		Assert.True(L.TryCall(0, 1).IsOk);
		Assert.True(L.TryReadInteger(-1, out long product));
		Assert.Equal(42, product);
		L.Pop(1);

		Assert.True(L.TryExecute("return 1, 2, 3"u8, LuaState.MultipleResults).IsOk);
		Assert.Equal(3, L.Top);
		L.Pop(3);

		LuaStatus runtime = L.TryExecute("local t = nil; return t.x"u8, 1, "=script"u8);
		Assert.Equal(LuaStatus.RuntimeError, runtime);
		Assert.StartsWith("script:1:", LuaError.FromStack(L, runtime).Message, StringComparison.Ordinal);
		Assert.Equal(1, L.Top);
	}

	[Fact]
	public void Binary_chunks_are_refused()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		LuaStatus status = L.TryLoad("Lua"u8);

		Assert.Equal(LuaStatus.SyntaxError, status);
	}

	[Fact]
	public void Message_handler_variant_transforms_the_error_value()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, "return function(message) return 'handled: ' .. message end"u8, 1);
		Assert.True(L.TryLoad("error('inner')"u8, "=t"u8).IsOk);

		LuaStatus status = L.TryCall(0, 0, 1);

		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.StartsWith("handled: t:1: inner", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void Error_values_that_are_not_strings_are_described_not_converted()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);

		LuaStatus tableStatus =
			L.TryExecute("error(setmetatable({}, { __tostring = function() error('never call me') end }))"u8, 0);
		Assert.Equal(LuaStatus.RuntimeError, tableStatus);
		Assert.Equal("(error object is a table value)", LuaError.FromStack(L, tableStatus).Message);
		L.Pop(1);

		// Lua 5.3's error() tests lua_isstring, which a number passes: level 1 would prefix the position and turn the
		// value into a string. Level 0 keeps the number, which FromStack renders itself.
		LuaStatus numberStatus = L.TryExecute("error(404, 0)"u8, 0);
		Assert.Equal(LuaType.Number, L.TypeOf(-1));
		Assert.Equal("404", LuaError.FromStack(L, numberStatus).Message);
		L.Pop(1);

		LuaStatus nilStatus = L.TryExecute("error()"u8, 0);
		Assert.Equal("(error object is a nil value)", LuaError.FromStack(L, nilStatus).Message);
	}

	[Fact]
	public void Throwing_form_raises_a_lua_exception_and_leaves_the_error_for_the_frame()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);

		LuaException exception = Assert.Throws<LuaException>(() => Run(L));

		Assert.Equal(LuaStatus.RuntimeError, exception.Status);
		Assert.Contains("thrown from lua", exception.Message, StringComparison.Ordinal);
		Assert.Equal(0, L.Top);

		static void Run(LuaState L)
		{
			using LuaFrame frame = new(L);
			L.TryExecute("error('thrown from lua')"u8, 0).ThrowIfFailed(L);
		}
	}

	[Fact]
	public void Helpers_survive_a_script_that_redefines_error_and_tostring()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		// Force the helpers in before the redefinition, as a plugin would when it makes its first protected call.
		Assert.True(L.TryGetGlobal("print"u8).IsOk);
		L.Pop(1);
		LuaTest.Run(L, "error = nil; tostring = function() return 'hijacked' end; return 5"u8, 1);

		Assert.True(L.TryToString(1).IsOk);
		Assert.Equal("5", LuaTest.ReadString(L, -1));
	}
}
