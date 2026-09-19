using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Api;

namespace CheatEngine.SDK.Lua.Interop.Types;

/// <summary>
///     Activation record of the Lua debug interface, filled by <see cref="LuaApi.lua_getstack" /> and
///     <see cref="LuaApi.lua_getinfo" /> and passed to hook functions. 128 bytes on x64.
/// </summary>
/// <remarks>
///     The layout follows the Lua 5.3 declaration with <c>LUA_IDSIZE</c> = 60 (<see cref="LuaApi.LUA_IDSIZE" />), the
///     value
///     of a default build and of Cheat Engine's SDK headers. On x64 every <c>LUA_IDSIZE</c> from 57 to 64 gives this same
///     layout (<see cref="short_src" /> at offset 56, <see cref="i_ci" /> aligned to 120): a library compiled with a value
///     above 64 would write past the end of this struct, one compiled with 56 or less would keep <see cref="i_ci" /> at a
///     lower offset. All string pointers are borrowed from the Lua state, NUL-terminated, and only valid while the
///     function they describe is alive. The caller owns the storage of the struct itself (normally a local).
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct lua_Debug
{
    /// <summary>Hook event code (<c>LUA_HOOK*</c>); only meaningful inside a hook.</summary>
    public int @event;

    /// <summary>("n") A plausible name for the function, or null when none can be found.</summary>
    public byte* name;

    /// <summary>("n") How <see cref="name" /> was found: "global", "local", "method", "field", "upvalue" or "".</summary>
    public byte* namewhat;

    /// <summary>("S") "Lua", "C" or "main".</summary>
    public byte* what;

    /// <summary>("S") Chunk name: "@file", "=custom" or the source text itself.</summary>
    public byte* source;

    /// <summary>("l") Line being executed, or -1 when unavailable.</summary>
    public int currentline;

    /// <summary>("S") First line of the function definition.</summary>
    public int linedefined;

    /// <summary>("S") Last line of the function definition.</summary>
    public int lastlinedefined;

    /// <summary>("u") Number of upvalues.</summary>
    public byte nups;

    /// <summary>("u") Number of fixed parameters (0 for C functions).</summary>
    public byte nparams;

    /// <summary>("u") Non-zero when the function is vararg (always for C functions).</summary>
    public sbyte isvararg;

    /// <summary>("t") Non-zero when the function was entered through a tail call.</summary>
    public sbyte istailcall;

    /// <summary>("S") Printable, NUL-terminated form of <see cref="source" /> for messages.</summary>
    public fixed byte short_src[LuaApi.LUA_IDSIZE];

    /// <summary>Private to Lua (the active <c>CallInfo</c>). Written by <see cref="LuaApi.lua_getstack" />; never touch it.</summary>
    public void* i_ci;
}
