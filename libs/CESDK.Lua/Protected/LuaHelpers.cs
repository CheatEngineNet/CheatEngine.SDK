using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CESDK.Lua.Callbacks;
using CESDK.Lua.Calls;
using CESDK.Lua.Interop.Types;
using CESDK.Lua.Interop.Protected;
using CESDK.Lua.State;
using static CESDK.Lua.Interop.Api.LuaApi;

namespace CESDK.Lua.Protected;

/// <summary>
///     The helper functions behind every protected operation, and the sentinel of the managed-to-Lua error channel.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why Lua functions and not C closures.</b> A protected <c>getfield</c> needs a function to run under
///         <c>lua_pcallk</c>. A managed <c>[UnmanagedCallersOnly]</c> closure cannot be that function: the raising API it
///         would call unwinds with <c>longjmp</c> straight through the managed frame back to <c>lua_pcallk</c>, which is
///         the
///         one thing this SDK must never let happen. A function written in Lua has no managed frame; when it raises, only
///         Lua frames are unwound and the status comes back as a return value.
///     </para>
///     <para>
///         <b>Where they live.</b> The chunk <see cref="Source" /> is compiled once per Lua state, lazily, on the first
///         protected operation, and its functions are stored in that state's registry under light-userdata keys: the
///         addresses of the bytes of a small block of memory owned by this type (the idiom of the Lua manual for
///         library-private registry entries). Keying by state rather than by managed epoch means a state that Cheat Engine
///         resets, a fresh test state, or a second SDK copy in another load context each get their own set without
///         coordination. Per operation the cost is one <c>lua_rawgetp</c> (never raises) whose returned type tag doubles
///         as
///         the "installed?" check.
///     </para>
///     <para>
///         <b>Why the key block is not a static struct field.</b> The keys are compared as addresses for the whole life of
///         the load context, so they must never move. The storage of a static value-type field may live on the GC heap and
///         may be relocated by a compacting collection (Microsoft Learn, <c>FixedAddressValueTypeAttribute</c> remarks); a
///         moved block would silently install a second helper set and, worse, make
///         <see cref="LuaThunk.Fail(LuaState,System.ReadOnlySpan{byte})" />
///         push a sentinel that no wrapper recognizes. <see cref="RuntimeHelpers.AllocateTypeAssociatedMemory" /> returns
///         memory that is tied to this type's load context, never moves and is freed only if the context unloads: one
///         allocation of <see cref="KeyBlockSize" /> bytes per SDK copy, never dereferenced, only addressed. A second
///         plugin
///         loads its own copy of this type and therefore gets its own block, so two plugins never collide on a key.
///     </para>
///     <para>
///         <b>The sentinel.</b> A light userdata whose value is the address of the last byte of the block. Lua code cannot
///         forge a light userdata, so a callback returning <c>(sentinel, message)</c> is unambiguous; the <c>check</c>
///         function in the chunk compares against the copy it received when the chunk ran.
///     </para>
/// </remarks>
internal static unsafe class LuaHelpers
{
    /// <summary>Number of functions the chunk returns.</summary>
    internal const int Count = (int)LuaHelper.Next + 1;

    /// <summary>
    ///     Free stack slots <see cref="Install" /> needs above the caller's top: the chunk, its argument, and
    ///     <c>lua_pcallk</c>'s requirement that the frame has room for <c>nresults - nargs</c> more values
    ///     (<c>Count - 1</c>), rounded up by one.
    /// </summary>
    internal const int InstallStackSlots = Count + 2;

    // Key block: Count helper keys followed by the sentinel address. The bytes are only ever addressed.
    private const int SentinelOffset = Count;
    private const int KeyBlockSize = Count + 1;

    private static readonly nint SKeyBlock =
        RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(LuaHelpers), KeyBlockSize);

    /// <summary>
    ///     The helper chunk. It receives the sentinel as its single argument and returns the functions of
    ///     <see cref="LuaHelper" /> in enum order. Written for Lua 5.3: <c>_ENV</c> is the globals table of the state at
    ///     load time, <c>error</c>, <c>tostring</c> and <c>next</c> are captured once so later redefinitions by scripts
    ///     do not change the SDK's behaviour, and <c>error(message, 2)</c> blames the caller of the wrapped function.
    /// </summary>
    internal static ReadOnlySpan<byte> Source => """
                                                 local SENTINEL = ...
                                                 local error, tostring, next = error, tostring, next
                                                 local function check(...)
                                                   local first = ...
                                                   if first == SENTINEL then
                                                     local _, message = ...
                                                     error(message, 2)
                                                   end
                                                   return ...
                                                 end
                                                 return
                                                   function(f) return function(...) return check(f(...)) end end,
                                                   function(k) return _ENV[k] end,
                                                   function(k, v) _ENV[k] = v end,
                                                   function(o, k) return o[k] end,
                                                   function(o, k, v) o[k] = v end,
                                                   function(o) return #o end,
                                                   function(v) return tostring(v) end,
                                                   function(op, a, b)
                                                     if op == 0 then return a == b elseif op == 1 then return a < b else return a <= b end
                                                   end,
                                                   function(t, k) return next(t, k) end
                                                 """u8;

    /// <summary>Address used as the error-channel sentinel light userdata. Stable for the life of the load context.</summary>
    internal static nint SentinelAddress => SKeyBlock + SentinelOffset;

    /// <summary>Pushes the sentinel light userdata. Never raises.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PushSentinel(lua_State* l)
    {
        lua_pushlightuserdata(l, (void*)SentinelAddress);
    }

    /// <summary>Whether the value at <paramref name="index" /> is the sentinel.</summary>
    internal static bool IsSentinel(lua_State* l, int index)
    {
        return lua_type(l, index) == LUA_TLIGHTUSERDATA && (nint)lua_touserdata(l, index) == SentinelAddress;
    }

    /// <summary>
    ///     Pushes the helper <paramref name="helper" /> of the state, compiling and installing the chunk on first use.
    ///     Stack: +1 on success (the function); +1 on failure (the error value of the failed load or run).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuaStatus Push(lua_State* l, LuaHelper helper)
    {
        if (lua_rawgetp(l, LUA_REGISTRYINDEX, Key(helper)) == LUA_TFUNCTION) return LuaStatus.Ok;

        lua_settop(l, -2);
        return InstallAndPush(l, helper);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LuaStatus InstallAndPush(lua_State* l, LuaHelper helper)
    {
        var status = Install(l);
        if (!status.IsOk) return status;

        var type = lua_rawgetp(l, LUA_REGISTRYINDEX, Key(helper));
        Debug.Assert(type == LUA_TFUNCTION, "The helper chunk ran but did not install this helper.");
        return LuaStatus.Ok;
    }

    /// <summary>
    ///     Compiles and runs the chunk on <paramref name="L" /> and stores its results in the registry. Stack: +0 on
    ///     success, +1 (error value) on failure. Safe to run again on a state that already has the helpers: the new set
    ///     replaces the old one.
    /// </summary>
    /// <remarks>
    ///     The C API requires the caller of <c>lua_pcallk</c> to have room for the results
    ///     (<c>lapi.c</c>, <c>checkresults</c>), so the first protected operation in a state asks for
    ///     <see cref="InstallStackSlots" /> free slots; every later one needs at most four. <c>lua_checkstack</c> can only
    ///     fail when the stack is at its hard limit or the process is out of memory; then the one error value this
    ///     method still owes its caller is <c>nil</c>, because pushing a string would allocate.
    /// </remarks>
    internal static LuaStatus Install(lua_State* L)
    {
        if (lua_checkstack(L, InstallStackSlots) == 0)
        {
            lua_pushnil(L);
            return LuaStatus.MemoryError;
        }

        var top = lua_gettop(L);
        int loadStatus;
        fixed (byte* source = Source)
        fixed (byte* name = "=CESDK.Lua"u8)
        fixed (byte* mode = "t"u8)
        {
            loadStatus = luaL_loadbufferx(L, source, (nuint)Source.Length, name, mode);
        }

        if (loadStatus != LUA_OK) return new LuaStatus(loadStatus);

        PushSentinel(L);
        var runStatus = lua_pcallk(L, 1, Count, 0, 0, null);
        if (runStatus != LUA_OK) return new LuaStatus(runStatus);

        // The last result is on top; rawsetp pops the top each time, so the keys are assigned in reverse.
        for (var i = Count - 1; i >= 0; i--)
        {
            var status = new LuaStatus(LuaProtectedApi.RawSetP(L, LUA_REGISTRYINDEX, SKeyBlock + i));
            if (!status.IsOk) return new LuaState((nint)L).KeepProtectedError(top, status);
        }

        Debug.Assert(lua_gettop(L) == top, "Installing the helpers left the stack unbalanced.");
        return LuaStatus.Ok;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* Key(LuaHelper helper)
    {
        return (void*)(SKeyBlock + (int)helper);
    }
}
