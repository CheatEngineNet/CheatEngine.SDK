using System.Runtime.CompilerServices;
using System.Threading;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Protected;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.References;

/// <summary>Serializes references in this SDK copy's private table, isolated from the host registry free list.</summary>
internal static class LuaReferences
{
    internal static Lock Gate { get; } = new();

    internal static nint Key { get; } = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(LuaReferences), 1);

    internal static unsafe LuaStatus Create(LuaState state, out int reference)
    {
        lock (Gate)
        {
            return new LuaStatus(LuaProtectedApi.TryCreatePrivateRef(state.Pointer, Key, out reference));
        }
    }

    internal static unsafe bool Push(LuaState state, int reference)
    {
        // Neither raw lookup allocates or runs Lua code. The caller holds Gate across slot validation and this read.
        if (LuaApi.lua_rawgetp(state.Pointer, LuaApi.LUA_REGISTRYINDEX, (void*)Key) != LuaApi.LUA_TTABLE)
        {
            state.Pop(1);
            return false;
        }

        _ = LuaApi.lua_rawgeti(state.Pointer, -1, reference);
        state.Remove(-2);
        return true;
    }

    internal static unsafe void Release(LuaState state, int reference)
    {
        state.CheckProtectedResult(new LuaStatus(LuaProtectedApi.UnrefPrivate(state.Pointer, Key, reference)));
    }
}
