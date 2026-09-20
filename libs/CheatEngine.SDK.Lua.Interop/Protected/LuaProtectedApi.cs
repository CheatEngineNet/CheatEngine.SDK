using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Protected;

// The bridge is intentionally internal. Higher layers own the synchronization and stack contracts;
// this type only transfers one operation through Lua's native protected boundary.
internal static unsafe partial class LuaProtectedApi
{
    // Native bridge could not reserve even its one closure slot; unlike Lua statuses, no error object was pushed.
    internal const int NoErrorStatus = -100;
    private const string BridgeLibrary = "cheatengine-sdk-lua-bridge";

    private static readonly string[] s_requiredBridgeExports =
    [
        "cheatengine_sdk_lua_protected",
        "cheatengine_sdk_lua_bridge_abi_version",
        "cheatengine_sdk_lua_bridge_get_contract",
        "cheatengine_sdk_lua_bridge_source_fingerprint"
    ];

    private static readonly Lock s_gate = new();
    private static BridgeBinding? s_binding;

    internal static int PushBytes(lua_State* state, ReadOnlySpan<byte> bytes)
    {
        fixed (byte* data = bytes)
        {
            return Invoke(state, LuaProtectedOperation.PushBytes, 0, data, (nuint)bytes.Length, 0, 0);
        }
    }

    internal static int PushByteTable(lua_State* state, ReadOnlySpan<byte> bytes)
    {
        fixed (byte* data = bytes)
        {
            return Invoke(state, LuaProtectedOperation.PushByteTable, 0, data, (nuint)bytes.Length, 0, 0);
        }
    }

    internal static int CreateTable(lua_State* state, int arrayCapacity, int recordCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(arrayCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(recordCapacity);
        return Invoke(state, LuaProtectedOperation.CreateTable, 0, null, 0, arrayCapacity, recordCapacity);
    }

    internal static int NewUserdata(lua_State* state, nuint bytes)
    {
        return Invoke(state, LuaProtectedOperation.NewUserdata, 0, null, bytes, 0, 0);
    }

    internal static int PushClosure(lua_State* state, nint function, int upvalues)
    {
        if (function == 0)
            throw new ArgumentException("The Lua C function must not be zero.", nameof(function));
        if ((uint)upvalues > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(upvalues),
                "Lua 5.3 supports between zero and 255 closure upvalues.");

        return Invoke(state, LuaProtectedOperation.PushClosure, upvalues, (void*)function, 0, upvalues, 0);
    }

    internal static int PushHostObject(lua_State* state, nint hostObjectPusher, nint nativeObject)
    {
        if (hostObjectPusher == 0)
            throw new ArgumentException("The host-object pusher must not be zero.", nameof(hostObjectPusher));

        return Invoke(state, LuaProtectedOperation.PushHostObject, 0, (void*)hostObjectPusher, 0, nativeObject, 0);
    }

    internal static int RawSet(lua_State* state, int tableIndex)
    {
        var binding = ValidateInvocation(state, 2);
        var top = PushTableBeforeInputs(state, tableIndex, 2);
        try
        {
            return Invoke(binding, state, LuaProtectedOperation.RawSet, 3, null, 0, 1, 0);
        }
        catch (Exception)
        {
            RollbackTableInput(state, top, 2);
            throw;
        }
    }

    internal static int RawSetI(lua_State* state, int tableIndex, lua_Integer key)
    {
        var binding = ValidateInvocation(state, 1);
        var top = PushTableBeforeInputs(state, tableIndex, 1);
        try
        {
            return Invoke(binding, state, LuaProtectedOperation.RawSetIndex, 2, null, 0, 1, (nint)key);
        }
        catch (Exception)
        {
            RollbackTableInput(state, top, 1);
            throw;
        }
    }

    internal static int RawSetP(lua_State* state, int tableIndex, nint key)
    {
        var binding = ValidateInvocation(state, 1);
        var top = PushTableBeforeInputs(state, tableIndex, 1);
        try
        {
            return Invoke(binding, state, LuaProtectedOperation.RawSetPointer, 2, (void*)key, 0, 1, 0);
        }
        catch (Exception)
        {
            RollbackTableInput(state, top, 1);
            throw;
        }
    }

    internal static int TryCreatePrivateRef(lua_State* state, nint stableKey, out int reference)
    {
        var status = Invoke(state, LuaProtectedOperation.CreateReference, 1, GetPrivateReferenceKey(stableKey), 0, 0,
            0);
        if (status == LuaApi.LUA_OK)
        {
            reference = checked((int)LuaApi.lua_tointegerx(state, -1, null));
            LuaApi.lua_settop(state, -2);
        }
        else
        {
            reference = LuaApi.LUA_NOREF;
        }

        return status;
    }

    internal static int PushPrivateRef(lua_State* state, nint stableKey, int reference)
    {
        return Invoke(state, LuaProtectedOperation.PushReference, 0, GetPrivateReferenceKey(stableKey), 0, reference,
            0);
    }

    internal static int UnrefPrivate(lua_State* state, nint stableKey, int reference)
    {
        return Invoke(state, LuaProtectedOperation.ReleaseReference, 0, GetPrivateReferenceKey(stableKey), 0, reference,
            0);
    }

    private static int Invoke(lua_State* state, LuaProtectedOperation operation, int inputCount, void* data, nuint size,
        nint first, nint second)
    {
        var binding = ValidateInvocation(state, inputCount);
        return Invoke(binding, state, operation, inputCount, data, size, first, second);
    }

    private static int Invoke(BridgeBinding binding, lua_State* state, LuaProtectedOperation operation, int inputCount,
        void* data, nuint size, nint first, nint second)
    {
        if (!LuaProtectedOperationContract.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));

        var exports = binding.Exports;
        var status =
            cheatengine_sdk_lua_protected(state, in exports, (int)operation, inputCount, data, size, first, second);
        if (status == NoErrorStatus)
            throw new InvalidOperationException(
                "Lua could not reserve a stack slot for the protected operation; the stack is unchanged.");
        return status;
    }

    private static BridgeBinding ValidateInvocation(lua_State* state, int inputCount)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));
        ArgumentOutOfRangeException.ThrowIfNegative(inputCount);

        var binding = EnsureLoaded();
        if (inputCount > LuaApi.lua_gettop(state))
            throw new InvalidOperationException(
                "The Lua stack does not contain the inputs required by the protected operation.");
        return binding;
    }

    private static int PushTableBeforeInputs(lua_State* state, int tableIndex, int inputCount)
    {
        var top = LuaApi.lua_gettop(state);
        var absoluteTableIndex = GetValidTableIndex(tableIndex, top);
        if (LuaApi.lua_checkstack(state, 2) == 0)
            throw new InvalidOperationException(
                "Lua could not reserve stack slots for the protected operation; the stack is unchanged.");
        LuaApi.lua_pushvalue(state, absoluteTableIndex);
        LuaApi.lua_rotate(state, -(inputCount + 1), 1);
        return top;
    }

    private static void RollbackTableInput(lua_State* state, int top, int inputCount)
    {
        // [.. table key value] -> [.. key value table] -> [.. key value].
        LuaApi.lua_rotate(state, -(inputCount + 1), -1);
        LuaApi.lua_settop(state, top);
    }

    private static int GetValidTableIndex(int tableIndex, int top)
    {
        if (tableIndex == LuaApi.LUA_REGISTRYINDEX) return tableIndex;
        if (tableIndex <= LuaApi.LUA_REGISTRYINDEX || tableIndex == 0)
            throw new ArgumentOutOfRangeException(nameof(tableIndex),
                "The table index must be a valid stack index or LUA_REGISTRYINDEX.");

        var absoluteIndex = tableIndex > 0 ? tableIndex : (long)top + tableIndex + 1;
        if (absoluteIndex < 1 || absoluteIndex > top)
            throw new ArgumentOutOfRangeException(nameof(tableIndex),
                "The table index is outside the current Lua stack.");
        return (int)absoluteIndex;
    }

    private static void* GetPrivateReferenceKey(nint stableKey)
    {
        if (stableKey == 0)
            throw new ArgumentException("The private-reference key must not be zero.", nameof(stableKey));
        return (void*)stableKey;
    }

    private static BridgeBinding EnsureLoaded()
    {
        var module = LuaApi.ModuleHandle;
        if (module == 0) throw new InvalidOperationException("Bind LuaApi before invoking protected Lua operations.");
        var binding = Volatile.Read(ref s_binding);
        if (binding?.Module == module) return binding;
        lock (s_gate)
        {
            binding = s_binding;
            if (binding?.Module == module) return binding;
            ValidateBridgeContract();
            ValidateRequiredExports();
            binding = new BridgeBinding(module, LuaProtectedExports.Create(module));
            Volatile.Write(ref s_binding, binding);
            return binding;
        }
    }

    [LibraryImport(BridgeLibrary, EntryPoint = "cheatengine_sdk_lua_protected")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int cheatengine_sdk_lua_protected(lua_State* state, in LuaProtectedExports exports,
        int operation, int inputCount, void* data, nuint size, nint first, nint second);

    [LibraryImport(BridgeLibrary, EntryPoint = "cheatengine_sdk_lua_bridge_get_contract")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int cheatengine_sdk_lua_bridge_get_contract(out LuaBridgeContract contract,
        nuint contractSize);

    [LibraryImport(BridgeLibrary, EntryPoint = "cheatengine_sdk_lua_bridge_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint cheatengine_sdk_lua_bridge_abi_version();

    private static void ValidateBridgeContract()
    {
        LuaBridgeContract contract;
        try
        {
            if (cheatengine_sdk_lua_bridge_abi_version() != LuaBridgeContract.ExpectedLegacyAbiVersion)
                throw new InvalidOperationException(
                    "cheatengine-sdk-lua-bridge.dll has an incompatible legacy ABI version.");

            if (cheatengine_sdk_lua_bridge_get_contract(out contract, (nuint)Unsafe.SizeOf<LuaBridgeContract>()) == 0)
                throw new InvalidOperationException(
                    "cheatengine-sdk-lua-bridge.dll rejected the managed contract buffer.");
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new InvalidOperationException(
                "cheatengine-sdk-lua-bridge.dll is outdated: the versioned contract export is missing. Restore or rebuild the matching CheatEngine.SDK package.",
                exception);
        }

        if (!contract.IsCompatible())
            throw new InvalidOperationException(
                "cheatengine-sdk-lua-bridge.dll has an incompatible ABI contract. Restore or rebuild the matching CheatEngine.SDK package.");
    }

    private static void ValidateRequiredExports()
    {
        nint module = 0;
        try
        {
            module = NativeLibrary.Load(
                BridgeLibrary,
                typeof(LuaProtectedApi).Assembly,
                DllImportSearchPath.AssemblyDirectory);
            for (var i = 0; i < s_requiredBridgeExports.Length; i++)
                if (!NativeLibrary.TryGetExport(module, s_requiredBridgeExports[i], out _))
                    throw new InvalidOperationException(
                        $"cheatengine-sdk-lua-bridge.dll is missing required export '{s_requiredBridgeExports[i]}'. Restore or rebuild the matching CheatEngine.SDK package.");
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
        }
    }
}
