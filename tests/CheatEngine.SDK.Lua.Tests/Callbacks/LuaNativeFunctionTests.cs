using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Tests.Callbacks;

/// <summary>The function-address value type. No Lua library involved: nothing here calls the function.</summary>
public sealed unsafe class LuaNativeFunctionTests
{
	[Fact]
	public void Default_is_the_null_function()
	{
		LuaNativeFunction function = default;

		Assert.True(function.IsNull);
		Assert.Equal(0, function.Address);
		Assert.Equal("lua_CFunction@0x0", function.ToString());
	}

	[Fact]
	public void The_typed_constructor_and_the_address_constructor_agree()
	{
		delegate* unmanaged[Cdecl]<nint, int> byHandle = &ReturnZeroByHandle;
		// A thunk written against lua_State* has no typed constructor (no raw pointer in the public surface): its
		// address is passed as an integer.
		IntPtr byPointer = (nint) (delegate* unmanaged[Cdecl]<lua_State*, int>) &ReturnZeroByPointer;

		LuaNativeFunction a = new(byHandle);
		LuaNativeFunction b = new((nint) byHandle);
		LuaNativeFunction c = new(byPointer);

		Assert.False(a.IsNull);
		Assert.Equal(a, b);
		Assert.True(a == b);
		Assert.True(a != c);
		Assert.Equal(a.GetHashCode(), b.GetHashCode());
		Assert.Equal(byPointer, c.Address);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int ReturnZeroByHandle(nint L)
	{
		return 0;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int ReturnZeroByPointer(lua_State* L)
	{
		return 0;
	}
}
