using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Protected;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.Protected;

/// <summary>
///     Audit A05-06/A05-18: the bridge keeps its call context in a thread-local variable and restores the previous one
///     around <c>lua_pcallk</c>, so a protected operation started while another one runs returns to the outer operation
///     intact. The nested calls come from a managed host pusher that <c>OP_PUSH_HOST_OBJECT</c> invokes, as Cheat
///     Engine's pusher could re-enter the SDK.
/// </summary>
/// <remarks>
///     The pusher never raises a Lua error itself: that would <c>longjmp</c> over its managed frame. Its nested calls
///     are protected operations, whose failures come back as statuses.
/// </remarks>
[Trait("Category", "NativeLua")]
public sealed unsafe class NestedProtectedCallTests
{
	private const nint NativeObject = 0x5A1E;
	private const nint MissingReferenceTable = 0x5A1F;

	private static int s_nestedPushStatus = -1;
	private static int s_nestedFailureStatus = -1;
	private static int s_pusherTopDelta = int.MinValue;
	private static nint s_seenObject;
	private static bool s_pusherCaughtException;

	[Fact]
	public void Nested_protected_call_from_a_host_pusher_restores_the_outer_context()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;
		lua_pushinteger(L, 71);
		int top = lua_gettop(L);
		nint pusher = (nint) (delegate* unmanaged[Stdcall]<lua_State*, void*, void>) &NestingPusher;

		int status = LuaProtectedApi.PushHostObject(L, pusher, NativeObject);

		Assert.False(s_pusherCaughtException, "The nested protected calls threw a managed exception.");
		Assert.Equal(NativeObject, s_seenObject);
		Assert.Equal(LUA_OK, s_nestedPushStatus);
		Assert.Equal(LUA_ERRRUN, s_nestedFailureStatus);
		Assert.Equal(1, s_pusherTopDelta);
		Assert.Equal(LUA_OK, status);
		Assert.Equal(top + 1, lua_gettop(L));
		Assert.Equal("pushed by the nested call", LuaTest.ReadString(L, -1));
		Assert.Equal(71, lua_tointegerx(L, top, null));

		lua_settop(L, top);
		Assert.Equal(top, lua_gettop(L));
		Assert.Equal(LUA_OK, LuaProtectedApi.PushBytes(L, "after"u8));
		Assert.Equal("after", LuaTest.ReadString(L, -1));
		Assert.Equal(top + 1, lua_gettop(L));
	}

	/// <summary>
	///     A host pusher that pushes exactly one value through a nested protected operation, and contains the failure of
	///     a second nested operation whose native code raises with <c>lua_error</c> inside its own <c>lua_pcallk</c>.
	/// </summary>
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void NestingPusher(lua_State* L, void* nativeObject)
	{
		try
		{
			s_seenObject = (nint) nativeObject;
			int top = lua_gettop(L);
			s_nestedPushStatus = LuaProtectedApi.PushBytes(L, "pushed by the nested call"u8);
			s_nestedFailureStatus = LuaProtectedApi.PushPrivateRef(L, MissingReferenceTable, 1);
			if (s_nestedFailureStatus != LUA_OK)
			{
				lua_settop(L, -2);
			}

			s_pusherTopDelta = lua_gettop(L) - top;
		}
		catch (Exception)
		{
			s_pusherCaughtException = true;
		}
	}
}
