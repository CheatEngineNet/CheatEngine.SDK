using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// lua.h "comparison and arithmetic functions" plus the value operators of the "miscellaneous" block.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>void lua_arith (lua_State *L, int op)</c>. Applies an arithmetic or bitwise operator to the top one or two
    ///     values (second operand on top), with Lua semantics, and replaces them with the result.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="op">A <c>LUA_OP*</c> arithmetic operator.</param>
    /// <remarks>
    ///     Stack: -(2|1) +1. Raises: any (metamethods; also for plain operands, for example a bitwise operator on a
    ///     non-integral float).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_arith(lua_State* L, int op)
    {
        s_table.lua_arith(L, op);
    }

    /// <summary>
    ///     <c>int lua_rawequal (lua_State *L, int idx1, int idx2)</c>. 1 when the two values are primitively equal (no
    ///     <c>__eq</c>), 0 otherwise or when an index is not valid.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx1">Acceptable index of the first value.</param>
    /// <param name="idx2">Acceptable index of the second value.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_rawequal(lua_State* L, int idx1, int idx2)
    {
        return s_table.lua_rawequal(L, idx1, idx2);
    }

    /// <summary>
    ///     <c>int lua_compare (lua_State *L, int idx1, int idx2, int op)</c>. 1 when the comparison holds with Lua
    ///     semantics, 0 otherwise or when an index is not valid.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx1">Acceptable index of the left operand.</param>
    /// <param name="idx2">Acceptable index of the right operand.</param>
    /// <param name="op"><see cref="LUA_OPEQ" />, <see cref="LUA_OPLT" /> or <see cref="LUA_OPLE" />.</param>
    /// <remarks>Stack: -0 +0. Raises: any (metamethods; ordering values of different types).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_compare(lua_State* L, int idx1, int idx2, int op)
    {
        return s_table.lua_compare(L, idx1, idx2, op);
    }

    /// <summary>
    ///     <c>void lua_concat (lua_State *L, int n)</c>. Concatenates the top <paramref name="n" /> values into one; 0
    ///     pushes the empty string, 1 leaves the stack as is.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Number of values.</param>
    /// <remarks>Stack: -n +1. Raises: any (<c>__concat</c>, non-concatenable operands, memory).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_concat(lua_State* L, int n)
    {
        s_table.lua_concat(L, n);
    }

    /// <summary>
    ///     <c>void lua_len (lua_State *L, int idx)</c>. Pushes the length of the value with Lua semantics (<c>#</c>
    ///     operator, may call <c>__len</c>).
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index.</param>
    /// <remarks>Stack: -0 +1. Raises: any. <see cref="lua_rawlen" /> is the non-raising alternative.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_len(lua_State* L, int idx)
    {
        s_table.lua_len(L, idx);
    }

    /// <summary>
    ///     <c>size_t lua_stringtonumber (lua_State *L, const char *s)</c>. Parses a NUL-terminated numeral with the lexer's
    ///     rules and pushes the number. Returns the string size including the NUL on success, 0 (nothing pushed) on failure.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="s">NUL-terminated text.</param>
    /// <remarks>Stack: -0 +(0|1). Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static size_t lua_stringtonumber(lua_State* L, byte* s)
    {
        return s_table.lua_stringtonumber(L, s);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_arith;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int, int> lua_rawequal;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int, int, int> lua_compare;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_concat;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_len;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, size_t> lua_stringtonumber;

        private void LoadOperators(ref ExportResolver exports)
        {
            lua_arith = (delegate* unmanaged[Cdecl]<lua_State*, int, void>)exports.Resolve("lua_arith");
            lua_rawequal = (delegate* unmanaged[Cdecl]<lua_State*, int, int, int>)exports.Resolve("lua_rawequal");
            lua_compare = (delegate* unmanaged[Cdecl]<lua_State*, int, int, int, int>)exports.Resolve("lua_compare");
            lua_concat = (delegate* unmanaged[Cdecl]<lua_State*, int, void>)exports.Resolve("lua_concat");
            lua_len = (delegate* unmanaged[Cdecl]<lua_State*, int, void>)exports.Resolve("lua_len");
            lua_stringtonumber =
                (delegate* unmanaged[Cdecl]<lua_State*, byte*, size_t>)exports.Resolve("lua_stringtonumber");
        }
    }
}
