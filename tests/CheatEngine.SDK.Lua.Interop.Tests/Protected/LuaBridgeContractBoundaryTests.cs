using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Interop.Tests.Protected;

/// <summary>Verifies that the C11 bridge rejects every contract buffer shape except its exact ABI layout.</summary>
public sealed unsafe class LuaBridgeContractBoundaryTests
{
	private const uint ContractMagic = 0x4345534B;
	private const int NoErrorStatus = -100;
	private const int ProtectedExportCount = 20;
	private const int PushByteTableOperation = 11;
	private static nint s_forwardedPCall;
	private static int s_pcallCallCount;

	private static readonly string[] SProtectedExportNames =
	[
		"lua_gettop", "lua_settop", "lua_checkstack", "lua_rotate", "lua_pushlstring",
		"lua_pushinteger", "lua_createtable", "lua_newuserdata", "lua_pushcclosure",
		"lua_pushlightuserdata", "lua_rawset", "lua_rawseti", "lua_rawsetp", "lua_rawgetp",
		"lua_rawgeti", "lua_type", "lua_pcallk", "lua_error", "luaL_ref", "luaL_unref"
	];

	[Fact]
	public void Native_bridge_contract_rejects_invalid_buffers_and_zeroes_reserved_and_padding_bytes()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
		Assert.True(File.Exists(path), $"The native Lua bridge was not copied to '{path}'.");

		IntPtr module = NativeLibrary.Load(path);
		try
		{
			delegate* unmanaged[Cdecl]<LuaBridgeContract*, UIntPtr, int> getContract =
				(delegate* unmanaged[Cdecl]<LuaBridgeContract*, nuint, int>) NativeLibrary.GetExport(
					module,
					"cheatengine_sdk_lua_bridge_get_contract");
			delegate* unmanaged[Cdecl]<uint> abiVersion = (delegate* unmanaged[Cdecl]<uint>) NativeLibrary.GetExport(
				module,
				"cheatengine_sdk_lua_bridge_abi_version");
			UIntPtr size = (nuint) Unsafe.SizeOf<LuaBridgeContract>();
			LuaBridgeContract contract = default;

			Assert.Equal(1u, abiVersion());
			Assert.Equal(0, getContract(null, size));

			FillWithSentinel(&contract);
			Assert.Equal(0, getContract(&contract, size - 1));
			AssertAllBytesAreSentinel(&contract);

			Assert.Equal(0, getContract(&contract, size + 1));
			AssertAllBytesAreSentinel(&contract);

			Assert.Equal(1, getContract(&contract, size));
			Assert.Equal(ContractMagic, contract.Magic);
			Assert.Equal(checked((uint) size), contract.ContractSize);
			AssertReservedAndPaddingAreZero(&contract);
		}
		finally
		{
			NativeLibrary.Free(module);
		}
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Native_bridge_rejects_invalid_preconditions_without_mutating_the_Lua_stack()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* luaState = state.L;
		LuaApi.lua_pushinteger(luaState, 0x1CEB_00DA_5EED_1234);
		int top = LuaApi.lua_gettop(luaState);

		string path = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
		Assert.True(File.Exists(path), $"The native Lua bridge was not copied to '{path}'.");

		IntPtr module = NativeLibrary.Load(path);
		try
		{
			delegate* unmanaged[Cdecl]<lua_State*, IntPtr*, int, int, void*, UIntPtr, IntPtr, IntPtr, int>
				protectedOperation =
					(delegate* unmanaged[Cdecl]<lua_State*, nint*, int, int, void*, nuint, nint, nint, int>)
					NativeLibrary
						.GetExport(
							module,
							"cheatengine_sdk_lua_protected");
			IntPtr* completeExports = stackalloc nint[ProtectedExportCount];
			PopulateProtectedExports(LuaApi.ModuleHandle, completeExports);
			IntPtr* incompleteExports = stackalloc nint[ProtectedExportCount];
			for (int i = 0; i < ProtectedExportCount; i++)
			{
				incompleteExports[i] = completeExports[i];
			}

			incompleteExports[17] = 0; // lua_error is required before the bridge can enter lua_pcallk.

			Assert.Equal(NoErrorStatus, protectedOperation(luaState, incompleteExports, 0, 0, null, 0, 0, 0));
			AssertStackIsUnchanged(luaState, top);

			Assert.Equal(NoErrorStatus, protectedOperation(luaState, completeExports, 0, -1, null, 0, 0, 0));
			AssertStackIsUnchanged(luaState, top);

			Assert.Equal(NoErrorStatus, protectedOperation(luaState, completeExports, 0, top + 1, null, 0, 0, 0));
			AssertStackIsUnchanged(luaState, top);
		}
		finally
		{
			NativeLibrary.Free(module);
		}
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Native_bridge_builds_a_page_sized_byte_table_with_one_protected_call()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* luaState = state.L;
		byte[] bytes = new byte[4096];
		for (int index = 0; index < bytes.Length; index++)
		{
			bytes[index] = (byte) index;
		}

		string path = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
		Assert.True(File.Exists(path), $"The native Lua bridge was not copied to '{path}'.");

		IntPtr module = NativeLibrary.Load(path);
		try
		{
			delegate* unmanaged[Cdecl]<lua_State*, IntPtr*, int, int, void*, UIntPtr, IntPtr, IntPtr, int>
				protectedOperation =
					(delegate* unmanaged[Cdecl]<lua_State*, nint*, int, int, void*, nuint, nint, nint, int>)
					NativeLibrary
						.GetExport(
							module,
							"cheatengine_sdk_lua_protected");
			IntPtr* exports = stackalloc nint[ProtectedExportCount];
			PopulateProtectedExports(LuaApi.ModuleHandle, exports);
			s_forwardedPCall = exports[16];
			s_pcallCallCount = 0;
			exports[16] = (nint) (delegate* unmanaged[Cdecl]<lua_State*, int, int, int, nint, nint, int>) &CountPCall;

			fixed (byte* data = bytes)
			{
				Assert.Equal(LuaApi.LUA_OK, protectedOperation(
					luaState,
					exports,
					PushByteTableOperation,
					0,
					data,
					(nuint) bytes.Length,
					0,
					0));
			}

			AssertPageSizedByteTable(luaState, bytes);
		}
		finally
		{
			s_forwardedPCall = 0;
			s_pcallCallCount = 0;
			NativeLibrary.Free(module);
		}
	}

	private static void AssertPageSizedByteTable(lua_State* luaState, byte[] bytes)
	{
		Assert.Equal(1, s_pcallCallCount);
		Assert.Equal(1, LuaApi.lua_gettop(luaState));
		Assert.Equal((nuint) bytes.Length, LuaApi.lua_rawlen(luaState, -1));
		for (int index = 0; index < bytes.Length; index++)
		{
			Assert.Equal(LuaApi.LUA_TNUMBER, LuaApi.lua_rawgeti(luaState, -1, index + 1L));
			Assert.Equal(bytes[index], LuaApi.lua_tointeger(luaState, -1));
			LuaApi.lua_settop(luaState, -2);
		}

		Assert.Equal(1, LuaApi.lua_gettop(luaState));
		LuaApi.lua_settop(luaState, 0);
	}

	private static void PopulateProtectedExports(nint luaModule, nint* exports)
	{
		Assert.Equal(ProtectedExportCount, SProtectedExportNames.Length);
		for (int i = 0; i < SProtectedExportNames.Length; i++)
		{
			exports[i] = NativeLibrary.GetExport(luaModule, SProtectedExportNames[i]);
		}
	}

	private static void AssertStackIsUnchanged(lua_State* luaState, int expectedTop)
	{
		Assert.Equal(expectedTop, LuaApi.lua_gettop(luaState));
		Assert.Equal(0x1CEB_00DA_5EED_1234, LuaApi.lua_tointeger(luaState, -1));
	}

	private static void FillWithSentinel(LuaBridgeContract* contract)
	{
		Span<byte> bytes = new(contract, Unsafe.SizeOf<LuaBridgeContract>());
		bytes.Fill(0xA5);
	}

	private static void AssertAllBytesAreSentinel(LuaBridgeContract* contract)
	{
		ReadOnlySpan<byte> bytes = new(contract, Unsafe.SizeOf<LuaBridgeContract>());
		for (int i = 0; i < bytes.Length; i++)
		{
			Assert.Equal((byte) 0xA5, bytes[i]);
		}
	}

	private static void AssertReservedAndPaddingAreZero(LuaBridgeContract* contract)
	{
		ReadOnlySpan<byte> bytes = new(contract, Unsafe.SizeOf<LuaBridgeContract>());
		int reservedOffset = Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.Reserved)).ToInt32();
		Assert.Equal(4, bytes.Length - reservedOffset - sizeof(byte));
		for (int index = reservedOffset; index < bytes.Length; index++)
		{
			Assert.Equal((byte) 0, bytes[index]);
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int CountPCall(lua_State* luaState, int argumentCount, int resultCount, int errorFunction,
		nint context, nint continuation)
	{
		s_pcallCallCount++;
		return ((delegate* unmanaged[Cdecl]<lua_State*, int, int, int, nint, nint, int>) s_forwardedPCall)(
			luaState,
			argumentCount,
			resultCount,
			errorFunction,
			context,
			continuation);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct LuaBridgeContract
	{
		internal uint Magic;
		internal uint ContractSize;
		internal ulong SupportedOperations;
		internal uint ExportTableSize;
		internal ushort AbiMajor;
		internal ushort AbiMinor;
		internal byte PointerSize;
		internal byte LuaIntegerSize;
		internal byte SizeTSize;
		internal byte Reserved;
	}
}
