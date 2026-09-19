// C typedef equivalents, so that the declarations in this project read like lua.h / lauxlib.h and can be checked
// against them token by token. A using alias never leaves the assembly: consumers (and the XML documentation) see
// the expanded types.
//
// Scalars (luaconf.h of a default 64-bit build: LUA_INT_LONGLONG + LUA_REAL_DOUBLE, which is what Cheat Engine ships).

global using lua_Integer = long;
global using lua_KContext = nint;
global using lua_Number = double;
global using size_t = nuint;

// Function-pointer typedefs. Lua is cdecl; the convention is a no-op on x64 but matters on x86, so it is spelled out.
global using unsafe lua_Alloc = delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*>;
global using unsafe lua_CFunction = delegate* unmanaged[Cdecl]<CheatEngine.SDK.Lua.Interop.Types.lua_State*, int>;
global using unsafe lua_Hook =
    delegate* unmanaged[Cdecl]<
        CheatEngine.SDK.Lua.Interop.Types.lua_State*, CheatEngine.SDK.Lua.Interop.Types.lua_Debug*, void>;
global using unsafe lua_KFunction =
    delegate* unmanaged[Cdecl]<CheatEngine.SDK.Lua.Interop.Types.lua_State*, int, nint, int>;
global using unsafe lua_Reader =
    delegate* unmanaged[Cdecl]<CheatEngine.SDK.Lua.Interop.Types.lua_State*, void*, nuint*, byte*>;
global using unsafe lua_Writer =
    delegate* unmanaged[Cdecl]<CheatEngine.SDK.Lua.Interop.Types.lua_State*, void*, nuint, void*, int>;
