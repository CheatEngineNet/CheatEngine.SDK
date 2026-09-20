using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Interop.Tests.NativeProtection;

/// <summary>Verifies that the C11 bridge rejects every contract buffer shape except its exact ABI layout.</summary>
public sealed unsafe class LuaBridgeContractBoundaryTests
{
    private const uint ContractMagic = 0x4345534B;
    private const int NoErrorStatus = -100;
    private const int ProtectedExportCount = 20;
    private static readonly string[] s_protectedExportNames =
    [
        "lua_gettop", "lua_settop", "lua_checkstack", "lua_rotate", "lua_pushlstring",
        "lua_pushinteger", "lua_createtable", "lua_newuserdata", "lua_pushcclosure",
        "lua_pushlightuserdata", "lua_rawset", "lua_rawseti", "lua_rawsetp", "lua_rawgetp",
        "lua_rawgeti", "lua_type", "lua_pcallk", "lua_error", "luaL_ref", "luaL_unref"
    ];

    [Fact]
    public void Native_bridge_contract_rejects_null_small_and_large_buffers_without_writing_them()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
        Assert.True(File.Exists(path), $"The native Lua bridge was not copied to '{path}'.");

        var module = NativeLibrary.Load(path);
        try
        {
            var getContract = (delegate* unmanaged[Cdecl]<LuaBridgeContract*, nuint, int>)NativeLibrary.GetExport(
                module,
                "cheatengine_sdk_lua_bridge_get_contract");
            var abiVersion = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(
                module,
                "cheatengine_sdk_lua_bridge_abi_version");
            var size = (nuint)Unsafe.SizeOf<LuaBridgeContract>();
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
            Assert.Equal((uint)size, contract.ContractSize);
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
        var top = LuaApi.lua_gettop(luaState);

        var path = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
        Assert.True(File.Exists(path), $"The native Lua bridge was not copied to '{path}'.");

        nint module = NativeLibrary.Load(path);
        try
        {
            var protectedOperation =
                (delegate* unmanaged[Cdecl]<lua_State*, nint*, int, int, void*, nuint, nint, nint, int>)NativeLibrary.GetExport(
                    module,
                    "cheatengine_sdk_lua_protected");
            nint* completeExports = stackalloc nint[ProtectedExportCount];
            PopulateProtectedExports(LuaApi.ModuleHandle, completeExports);
            nint* incompleteExports = stackalloc nint[ProtectedExportCount];
            for (var i = 0; i < ProtectedExportCount; i++) incompleteExports[i] = completeExports[i];
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

    private static void PopulateProtectedExports(nint luaModule, nint* exports)
    {
        Assert.Equal(ProtectedExportCount, s_protectedExportNames.Length);
        for (var i = 0; i < s_protectedExportNames.Length; i++)
            exports[i] = NativeLibrary.GetExport(luaModule, s_protectedExportNames[i]);
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
        for (var i = 0; i < bytes.Length; i++)
            Assert.Equal((byte)0xA5, bytes[i]);
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
