using System.Diagnostics.CodeAnalysis;

namespace CESDK.Lua.State;

/// <summary>
///     The basic type of a Lua value, as reported by <see cref="LuaState.TypeOf" />. The numeric values are the type tags
///     of the Lua 5.3 C API (<c>LUA_TNONE</c> ... <c>LUA_TTHREAD</c>).
/// </summary>
/// <remarks>
///     Lua has one number type with two representations: use <see cref="LuaState.IsInteger" /> to tell an integer from a
///     float. A Cheat Engine object on the stack is a <see cref="Userdata" />.
/// </remarks>
public enum LuaType
{
    /// <summary>The index is acceptable but refers to no value (beyond the top of the stack).</summary>
    None = -1,

    /// <summary><c>nil</c>.</summary>
    Nil = 0,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean = 1,

    /// <summary>A raw pointer value without identity, metatable or lifetime of its own.</summary>
    LightUserdata = 2,

    /// <summary>An integer (64-bit) or a float (double).</summary>
    Number = 3,

    /// <summary>An immutable, length-counted byte string. This SDK treats the bytes as UTF-8.</summary>
    [SuppressMessage("Naming", "CA1720:Identifiers should not contain type names",
        Justification = "The Lua type is called string; renaming the tag would hide the C API it mirrors.")]
    String = 4,

    /// <summary>A table.</summary>
    Table = 5,

    /// <summary>A Lua function or a C function.</summary>
    Function = 6,

    /// <summary>A block of memory owned by Lua, optionally with a metatable. Host objects are of this type.</summary>
    Userdata = 7,

    /// <summary>A coroutine.</summary>
    Thread = 8
}
