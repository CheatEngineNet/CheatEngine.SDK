using CheatEngine.SDK.Lua.Interop.Api;

namespace CheatEngine.SDK.Lua.Interop.Types;

/// <summary>
///     Opaque Lua state (a main state or a coroutine thread). It has no managed representation: the type only exists so
///     that <c>lua_State*</c> is a distinct pointer type in signatures.
/// </summary>
/// <remarks>
///     Ownership: a state returned by <see cref="LuaApi.luaL_newstate" /> or <see cref="LuaApi.lua_newstate" /> belongs to
///     the caller until <see cref="LuaApi.lua_close" />; a thread returned by <see cref="LuaApi.lua_newthread" /> is owned
///     by the Lua garbage collector; every state handed out by Cheat Engine is borrowed. Thread affinity: one OS thread at
///     a time per global state, Lua has no locking of its own. Never dereference the pointer and never instantiate the
///     struct.
/// </remarks>
public struct lua_State;
