using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Lua.Protected;

/// <summary>
///     The Lua-side helper functions this assembly compiles once per Lua state and stores in that state's registry.
///     Every value is the position of the function among the results of the helper chunk (see <see cref="LuaHelpers" />)
///     and the offset of its registry key.
/// </summary>
internal enum LuaHelper
{
    /// <summary>
    ///     <c>function(f)</c>: returns a closure that calls <c>f</c> and turns the error-channel sentinel into
    ///     <c>error(message, 2)</c>.
    /// </summary>
    Wrap = 0,

    /// <summary><c>function(k)</c>: <c>return _ENV[k]</c>.</summary>
    GetGlobal = 1,

    /// <summary><c>function(k, v)</c>: <c>_ENV[k] = v</c>.</summary>
    SetGlobal = 2,

    /// <summary><c>function(o, k)</c>: <c>return o[k]</c>.</summary>
    Index = 3,

    /// <summary><c>function(o, k, v)</c>: <c>o[k] = v</c>.</summary>
    NewIndex = 4,

    /// <summary><c>function(o)</c>: <c>return #o</c>.</summary>
    Length = 5,

    /// <summary><c>function(v)</c>: <c>return tostring(v)</c>, with the <c>tostring</c> captured when the chunk ran.</summary>
    ToString = 6,

    /// <summary><c>function(op, a, b)</c>: <c>==</c>, <c>&lt;</c> or <c>&lt;=</c> by <see cref="LuaComparison" />.</summary>
    Compare = 7,

    /// <summary><c>function(t, k)</c>: <c>return next(t, k)</c>, with the <c>next</c> captured when the chunk ran.</summary>
    Next = 8
}
