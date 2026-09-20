using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Protected;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.NativeProtection;

/// <summary>Managed preconditions that prevent an invalid call from reaching bridge setup before <c>lua_pcallk</c>.</summary>
public sealed unsafe class LuaProtectedApiTests
{
    [Fact]
    public void Contract_has_the_exact_managed_C11_layout()
    {
        Assert.Equal(LuaBridgeContract.Size, Unsafe.SizeOf<LuaBridgeContract>());
        Assert.Equal(1u, LuaBridgeContract.ExpectedLegacyAbiVersion);
        Assert.Equal(0, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.Magic)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.ContractSize)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.SupportedOperations)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.ExportTableSize)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.AbiMajor)).ToInt32());
        Assert.Equal(22, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.AbiMinor)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.PointerSize)).ToInt32());
        Assert.Equal(25, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.LuaIntegerSize)).ToInt32());
        Assert.Equal(26, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.SizeTSize)).ToInt32());
        Assert.Equal(27, Marshal.OffsetOf<LuaBridgeContract>(nameof(LuaBridgeContract.Reserved)).ToInt32());
        Assert.Equal(20 * IntPtr.Size, Unsafe.SizeOf<LuaProtectedExports>());
    }

    [Fact]
    public void Contract_requires_all_compatible_C11_fields()
    {
        var contract = CreateCompatibleContract();
        Assert.True(contract.IsCompatible());

        contract.Magic = 0;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.ContractSize--;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.ExportTableSize--;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.AbiMajor++;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.AbiMinor = LuaBridgeContract.MinimumMinor - 1;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.SupportedOperations &= ~LuaProtectedOperationContract.RequiredBitmap;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.PointerSize--;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.LuaIntegerSize--;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.SizeTSize--;
        Assert.False(contract.IsCompatible());
        contract = CreateCompatibleContract();
        contract.Reserved = 1;
        Assert.False(contract.IsCompatible());
    }

    [Fact]
    public void Contract_accepts_a_newer_minor_version_and_additive_operation_bits()
    {
        var contract = CreateCompatibleContract();

        contract.AbiMinor = checked(LuaBridgeContract.MinimumMinor + 1);
        contract.SupportedOperations |= 1UL << 63;

        Assert.True(contract.IsCompatible());
    }

    [Fact]
    public void Protected_bridge_imports_are_explicit_cdecl_and_do_not_suppress_GC_transitions()
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        HashSet<string> expected =
        [
            "cheatengine_sdk_lua_protected",
            "cheatengine_sdk_lua_bridge_get_contract",
            "cheatengine_sdk_lua_bridge_abi_version"
        ];
        var importCount = 0;

        foreach (var method in typeof(LuaProtectedApi).GetMethods(Flags))
        {
            var libraryImport = method.GetCustomAttribute<LibraryImportAttribute>();
            if (libraryImport is null) continue;

            importCount++;
            Assert.True(expected.Remove(method.Name), $"Unexpected LibraryImport method '{method.Name}'.");
            Assert.Equal(method.Name, libraryImport.EntryPoint);

            var callConvention = method.GetCustomAttribute<UnmanagedCallConvAttribute>();
            Assert.NotNull(callConvention);
            Assert.NotNull(callConvention.CallConvs);
            Assert.Single(callConvention.CallConvs);
            Assert.Equal(typeof(CallConvCdecl), callConvention.CallConvs[0]);
            Assert.Null(method.GetCustomAttribute<SuppressGCTransitionAttribute>());
        }

        Assert.Equal(3, importCount);
        Assert.Empty(expected);
    }

    [Fact]
    public void Protected_operation_numbers_are_explicit_and_contiguous()
    {
        Assert.Equal(0, (int)LuaProtectedOperation.PushBytes);
        Assert.Equal(10, (int)LuaProtectedOperation.PushHostObject);
        Assert.Equal(11, (int)LuaProtectedOperation.PushByteTable);
        Assert.Equal(12, LuaProtectedOperationContract.Count);
        Assert.Equal((1UL << LuaProtectedOperationContract.Count) - 1, LuaProtectedOperationContract.RequiredBitmap);
        Assert.False(LuaProtectedOperationContract.IsDefined((LuaProtectedOperation)(-1)));
        Assert.False(
            LuaProtectedOperationContract.IsDefined((LuaProtectedOperation)LuaProtectedOperationContract.Count));
    }

    [Fact]
    public void CreateTable_negative_capacities_are_rejected_before_native_binding()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LuaProtectedApi.CreateTable(null, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LuaProtectedApi.CreateTable(null, 0, -1));
    }

    [Fact]
    public void PushClosure_invalid_shape_is_rejected_before_native_binding()
    {
        Assert.Throws<ArgumentException>(() => LuaProtectedApi.PushClosure(null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LuaProtectedApi.PushClosure(null, 1, 256));
    }

    [Fact]
    public void Protected_operations_reject_a_null_state_before_loading_the_bridge()
    {
        Assert.Throws<ArgumentNullException>(() => LuaProtectedApi.PushBytes(null, []));
        Assert.Throws<ArgumentNullException>(() => LuaProtectedApi.PushByteTable(null, []));
        Assert.Throws<ArgumentNullException>(() => LuaProtectedApi.NewUserdata(null, 1));
        Assert.Throws<ArgumentNullException>(() => LuaProtectedApi.PushHostObject(null, 1, 0));
    }

    [Fact]
    public void Private_references_reject_the_zero_registry_key_before_native_binding()
    {
        var reference = 17;
        Assert.Throws<ArgumentException>(() => LuaProtectedApi.TryCreatePrivateRef(null, 0, out reference));
        Assert.Throws<ArgumentException>(() => LuaProtectedApi.PushPrivateRef(null, 0, 1));
        Assert.Throws<ArgumentException>(() => LuaProtectedApi.UnrefPrivate(null, 0, 1));
        Assert.Equal(17, reference);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Private_reference_round_trip_creates_and_pushes_the_original_value()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var pointer = state.L;
        var stableKey = (nint)0x5A17;
        const long expected = -9_876_543_210;

        lua_pushinteger(pointer, expected);
        Assert.Equal(LUA_OK, LuaProtectedApi.TryCreatePrivateRef(pointer, stableKey, out var reference));
        Assert.True(reference > 0); // The SDK-private table owns its own luaL_ref free list.
        Assert.Equal(0, lua_gettop(pointer));

        Assert.Equal(LUA_OK, LuaProtectedApi.PushPrivateRef(pointer, stableKey, reference));
        Assert.Equal(1, lua_gettop(pointer));
        Assert.Equal(LUA_TNUMBER, lua_type(pointer, -1));
        Assert.Equal(expected, lua_tointeger(pointer, -1));

        lua_settop(pointer, 0);
        Assert.Equal(LUA_OK, LuaProtectedApi.UnrefPrivate(pointer, stableKey, reference));
        Assert.Equal(0, lua_gettop(pointer));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Push_private_reference_without_its_table_returns_an_error_and_preserves_the_caller_stack()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var pointer = state.L;
        var stableKey = (nint)0x5A18;
        const long sentinel = 71;

        lua_pushinteger(pointer, sentinel);
        var callerTop = lua_gettop(pointer);

        Assert.Equal(LUA_ERRRUN, LuaProtectedApi.PushPrivateRef(pointer, stableKey, 1));
        Assert.Equal(callerTop + 1, lua_gettop(pointer));
        Assert.Equal(sentinel, lua_tointeger(pointer, 1));
        Assert.Contains("private reference table is unavailable", LuaTest.ReadString(pointer, -1),
            StringComparison.Ordinal);

        lua_settop(pointer, callerTop);
        Assert.Equal(callerTop, lua_gettop(pointer));
        Assert.Equal(sentinel, lua_tointeger(pointer, -1));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Protected_operations_reject_missing_stack_inputs_before_bridge_setup()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var statePointer = (nint)state.L;
        Assert.Equal(0, lua_gettop((lua_State*)statePointer));

        Assert.Throws<InvalidOperationException>(() => LuaProtectedApi.RawSet((lua_State*)statePointer, 1));
        Assert.Throws<InvalidOperationException>(() => LuaProtectedApi.PushClosure((lua_State*)statePointer, 1, 1));

        var reference = 17;
        Assert.Throws<InvalidOperationException>(() =>
            LuaProtectedApi.TryCreatePrivateRef((lua_State*)statePointer, 1, out reference));
        Assert.Equal(17, reference);
        Assert.Equal(0, lua_gettop((lua_State*)statePointer));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void RawSet_rejects_an_invalid_table_index_without_touching_the_stack()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var pointer = state.L;
        var statePointer = (nint)pointer;
        lua_pushinteger(pointer, 1);
        lua_pushinteger(pointer, 2);
        var top = lua_gettop(pointer);

        Assert.Throws<ArgumentOutOfRangeException>(() => LuaProtectedApi.RawSet((lua_State*)statePointer, 0));

        Assert.Equal(top, lua_gettop(pointer));
        Assert.Equal(2, lua_tointeger(pointer, -1));
        Assert.Equal(1, lua_tointeger(pointer, -2));
    }

    private static LuaBridgeContract CreateCompatibleContract()
    {
        return new LuaBridgeContract
        {
            Magic = LuaBridgeContract.ExpectedMagic,
            ContractSize = (uint)Unsafe.SizeOf<LuaBridgeContract>(),
            SupportedOperations = LuaProtectedOperationContract.RequiredBitmap,
            ExportTableSize = (uint)Unsafe.SizeOf<LuaProtectedExports>(),
            AbiMajor = LuaBridgeContract.ExpectedMajor,
            AbiMinor = LuaBridgeContract.MinimumMinor,
            PointerSize = (byte)IntPtr.Size,
            LuaIntegerSize = sizeof(long),
            SizeTSize = (byte)sizeof(nuint)
        };
    }
}
