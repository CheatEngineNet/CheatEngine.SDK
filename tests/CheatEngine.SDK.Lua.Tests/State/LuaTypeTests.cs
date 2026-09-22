using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Tests.State;

/// <summary>
///     The managed type tags against the C API constants, and the state view's value semantics. No Lua library
///     involved.
/// </summary>
public sealed class LuaTypeTests
{
	[Fact]
	public void Type_tags_match_the_c_api()
	{
		Assert.Equal(LuaApi.LUA_TNONE, (int) LuaType.None);
		Assert.Equal(LuaApi.LUA_TNIL, (int) LuaType.Nil);
		Assert.Equal(LuaApi.LUA_TBOOLEAN, (int) LuaType.Boolean);
		Assert.Equal(LuaApi.LUA_TLIGHTUSERDATA, (int) LuaType.LightUserdata);
		Assert.Equal(LuaApi.LUA_TNUMBER, (int) LuaType.Number);
		Assert.Equal(LuaApi.LUA_TSTRING, (int) LuaType.String);
		Assert.Equal(LuaApi.LUA_TTABLE, (int) LuaType.Table);
		Assert.Equal(LuaApi.LUA_TFUNCTION, (int) LuaType.Function);
		Assert.Equal(LuaApi.LUA_TUSERDATA, (int) LuaType.Userdata);
		Assert.Equal(LuaApi.LUA_TTHREAD, (int) LuaType.Thread);
	}

	[Fact]
	public void Constants_match_the_c_api()
	{
		Assert.Equal(LuaApi.LUA_MULTRET, LuaState.MultipleResults);
		Assert.Equal(LuaApi.LUA_MINSTACK, LuaState.MinimumFreeSlots);
		Assert.Equal(LuaApi.LUA_REGISTRYINDEX, LuaState.RegistryIndex);
		Assert.Equal(-1001000, LuaState.RegistryIndex);
	}

	[Fact]
	public void Comparison_values_match_the_c_api()
	{
		Assert.Equal(LuaApi.LUA_OPEQ, (int) LuaComparison.Equal);
		Assert.Equal(LuaApi.LUA_OPLT, (int) LuaComparison.Less);
		Assert.Equal(LuaApi.LUA_OPLE, (int) LuaComparison.LessOrEqual);
	}

	[Fact]
	public void Default_state_view_is_null_and_states_compare_by_pointer()
	{
		LuaState none = default;
		Assert.True(none.IsNull);
		Assert.Equal(0, none.Handle);
		Assert.Equal(new LuaState(0), none);
		LuaState first = new(0x1000);
		LuaState second = new(0x1000);
		Assert.True(first == second);
		Assert.True(first != new LuaState(0x2000));
		Assert.Equal(new LuaState(0x1000).GetHashCode(), new LuaState(0x1000).GetHashCode());
		Assert.Equal("lua_State@0x1000", new LuaState(0x1000).ToString());
	}

	[Fact]
	public void Relative_indices_shift_absolute_and_pseudo_indices_do_not()
	{
		Assert.Equal(-3, LuaState.Shift(-1, 2));
		Assert.Equal(-2, LuaState.Shift(-1, 1));
		Assert.Equal(3, LuaState.Shift(3, 2));
		Assert.Equal(LuaApi.LUA_REGISTRYINDEX, LuaState.Shift(LuaApi.LUA_REGISTRYINDEX, 2));
		Assert.Equal(LuaApi.lua_upvalueindex(1), LuaState.Shift(LuaApi.lua_upvalueindex(1), 2));
	}
}
