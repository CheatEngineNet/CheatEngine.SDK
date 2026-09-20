using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Lua.Interop.Protected;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct LuaProtectedExports
{
    private readonly nint _getTop,
        _setTop,
        _checkStack,
        _rotate,
        _pushLString,
        _pushInteger,
        _createTable,
        _newUserdata,
        _pushClosure,
        _pushLightUserdata,
        _rawSet,
        _rawSetI,
        _rawSetP,
        _rawGetP,
        _rawGetI,
        _type,
        _pCallK,
        _error,
        _lRef,
        _lUnref;

    private LuaProtectedExports(nint getTop, nint setTop, nint checkStack, nint rotate, nint pushLString,
        nint pushInteger, nint createTable, nint newUserdata, nint pushClosure, nint pushLightUserdata, nint rawSet,
        nint rawSetI, nint rawSetP, nint rawGetP, nint rawGetI, nint type, nint pCallK, nint error, nint lRef,
        nint lUnref)
    {
        (_getTop, _setTop, _checkStack, _rotate, _pushLString, _pushInteger, _createTable, _newUserdata, _pushClosure,
            _pushLightUserdata, _rawSet, _rawSetI, _rawSetP, _rawGetP, _rawGetI, _type, _pCallK, _error, _lRef,
            _lUnref) = (getTop, setTop, checkStack, rotate, pushLString, pushInteger, createTable, newUserdata,
            pushClosure, pushLightUserdata, rawSet, rawSetI, rawSetP, rawGetP, rawGetI, type, pCallK, error, lRef,
            lUnref);
    }

    internal static LuaProtectedExports Create(nint module)
    {
        return new LuaProtectedExports(Export(module, "lua_gettop"), Export(module, "lua_settop"),
            Export(module, "lua_checkstack"),
            Export(module, "lua_rotate"), Export(module, "lua_pushlstring"), Export(module, "lua_pushinteger"),
            Export(module, "lua_createtable"), Export(module, "lua_newuserdata"), Export(module, "lua_pushcclosure"),
            Export(module, "lua_pushlightuserdata"), Export(module, "lua_rawset"), Export(module, "lua_rawseti"),
            Export(module, "lua_rawsetp"), Export(module, "lua_rawgetp"), Export(module, "lua_rawgeti"),
            Export(module, "lua_type"), Export(module, "lua_pcallk"), Export(module, "lua_error"),
            Export(module, "luaL_ref"), Export(module, "luaL_unref"));
    }

    private static nint Export(nint module, string name)
    {
        return NativeLibrary.GetExport(module, name);
    }
}
