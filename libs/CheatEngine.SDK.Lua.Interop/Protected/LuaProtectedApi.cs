using System;
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
    private const uint ExpectedAbiVersion = 1;
    private const int PushBytesOperation = 0;
    private const int CreateTableOperation = 1;
    private const int NewUserdataOperation = 2;
    private const int PushClosureOperation = 3;
    private const int RawSetOperation = 4;
    private const int RawSetIndexOperation = 5;
    private const int RawSetPointerOperation = 6;
    private const int CreateReferenceOperation = 7;
    private const int PushReferenceOperation = 8;
    private const int ReleaseReferenceOperation = 9;
    private static readonly Lock s_gate = new();
    private static BridgeBinding? s_binding;

    internal static int PushBytes(lua_State* state, ReadOnlySpan<byte> bytes)
    {
        fixed (byte* data = bytes) return Invoke(state, PushBytesOperation, 0, data, (nuint)bytes.Length, 0, 0);
    }

    internal static int CreateTable(lua_State* state, int arrayCapacity, int recordCapacity) => Invoke(state, CreateTableOperation, 0, null, 0, arrayCapacity, recordCapacity);
    internal static int NewUserdata(lua_State* state, nuint bytes) => Invoke(state, NewUserdataOperation, 0, null, bytes, 0, 0);
    internal static int PushClosure(lua_State* state, nint function, int upvalues) => Invoke(state, PushClosureOperation, upvalues, (void*)function, 0, upvalues, 0);
    internal static int RawSet(lua_State* state, int tableIndex)
    {
        EnsureLoaded();
        var top = LuaApi.lua_gettop(state);
        PushTableBeforeInputs(state, tableIndex, 2);
        try { return Invoke(state, RawSetOperation, 3, null, 0, 1, 0); }
        catch (Exception) { RollbackTableInput(state, top, 2); throw; }
    }

    internal static int RawSetI(lua_State* state, int tableIndex, lua_Integer key)
    {
        EnsureLoaded();
        var top = LuaApi.lua_gettop(state);
        PushTableBeforeInputs(state, tableIndex, 1);
        try { return Invoke(state, RawSetIndexOperation, 2, null, 0, 1, (nint)key); }
        catch (Exception) { RollbackTableInput(state, top, 1); throw; }
    }

    internal static int RawSetP(lua_State* state, int tableIndex, nint key)
    {
        EnsureLoaded();
        var top = LuaApi.lua_gettop(state);
        PushTableBeforeInputs(state, tableIndex, 1);
        try { return Invoke(state, RawSetPointerOperation, 2, (void*)key, 0, 1, 0); }
        catch (Exception) { RollbackTableInput(state, top, 1); throw; }
    }

    internal static int TryCreatePrivateRef(lua_State* state, nint stableKey, out int reference)
    {
        var status = Invoke(state, CreateReferenceOperation, 1, (void*)stableKey, 0, 0, 0);
        if (status == LuaApi.LUA_OK)
        {
            reference = checked((int)LuaApi.lua_tointegerx(state, -1, null));
            LuaApi.lua_settop(state, -2);
        }
        else reference = LuaApi.LUA_NOREF;
        return status;
    }

    internal static int PushPrivateRef(lua_State* state, nint stableKey, int reference) => Invoke(state, PushReferenceOperation, 0, (void*)stableKey, 0, reference, 0);
    internal static int UnrefPrivate(lua_State* state, nint stableKey, int reference) => Invoke(state, ReleaseReferenceOperation, 0, (void*)stableKey, 0, reference, 0);

    private static int Invoke(lua_State* state, int operation, int inputCount, void* data, nuint size, nint first, nint second)
    {
        var binding = EnsureLoaded();
        var exports = binding.Exports;
        var status = cheatengine_sdk_lua_protected(state, in exports, operation, inputCount, data, size, first, second);
        if (status == NoErrorStatus)
            throw new InvalidOperationException("Lua could not reserve a stack slot for the protected operation; the stack is unchanged.");
        return status;
    }

    private static void PushTableBeforeInputs(lua_State* state, int tableIndex, int inputCount)
    {
        var absoluteTableIndex = LuaApi.lua_absindex(state, tableIndex);
        if (LuaApi.lua_checkstack(state, 2) == 0)
            throw new InvalidOperationException("Lua could not reserve stack slots for the protected operation; the stack is unchanged.");
        LuaApi.lua_pushvalue(state, absoluteTableIndex);
        LuaApi.lua_rotate(state, -(inputCount + 1), 1);
    }

    private static void RollbackTableInput(lua_State* state, int top, int inputCount)
    {
        // [.. table key value] -> [.. key value table] -> [.. key value].
        LuaApi.lua_rotate(state, -(inputCount + 1), -1);
        LuaApi.lua_settop(state, top);
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
            ValidateAbiVersion();
            binding = new BridgeBinding(module, LuaProtectedExports.Create(module));
            Volatile.Write(ref s_binding, binding);
            return binding;
        }
    }

    [LibraryImport("cheatengine-sdk-lua-bridge", EntryPoint = "cheatengine_sdk_lua_protected")]
    private static partial int cheatengine_sdk_lua_protected(lua_State* state, in LuaProtectedExports exports, int operation, int inputCount, void* data, nuint size, nint first, nint second);

    [LibraryImport("cheatengine-sdk-lua-bridge", EntryPoint = "cheatengine_sdk_lua_bridge_abi_version")]
    private static partial uint cheatengine_sdk_lua_bridge_abi_version();

    private static void ValidateAbiVersion()
    {
        uint actual;
        try
        {
            actual = cheatengine_sdk_lua_bridge_abi_version();
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new InvalidOperationException(
                "cheatengine-sdk-lua-bridge.dll is outdated: the ABI version export is missing. Restore or rebuild the matching CheatEngine.SDK package.",
                exception);
        }

        if (actual != ExpectedAbiVersion)
            throw new InvalidOperationException(
                $"cheatengine-sdk-lua-bridge.dll has ABI version {actual}; this CheatEngine.SDK build requires version {ExpectedAbiVersion}.");
    }
}
