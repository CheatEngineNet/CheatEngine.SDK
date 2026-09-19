using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using CheatEngine.SDK.Annotations.Lua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

// Reads: one or two C API calls each, none can raise, none modifies the stack. Every member takes an acceptable index.
public readonly unsafe partial struct LuaState
{
    /// <summary>
    ///     Reads the value at <paramref name="index" /> as a 64-bit integer (<c>lua_tointegerx</c>), with Lua's own
    ///     conversion rules: an integer, a float with an exact integral value, or a string Lua can convert.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <param name="value">The integer, or 0 when the value is not convertible.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> holds the converted value.</returns>
    /// <remarks>
    ///     Use <see cref="IsInteger" /> first when a float such as <c>3.0</c> must be rejected. The stack slot is not
    ///     modified.
    /// </remarks>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInteger(int index, out long value)
    {
        int isNumber;
        value = lua_tointegerx(Pointer, index, &isNumber);
        return isNumber != 0;
    }

    /// <summary>
    ///     Reads the value at <paramref name="index" /> as a double (<c>lua_tonumberx</c>): any number, or a string Lua
    ///     can convert.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <param name="value">The number, or 0 when the value is not convertible.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> holds the converted value.</returns>
    /// <remarks>The stack slot is not modified.</remarks>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadNumber(int index, out double value)
    {
        int isNumber;
        value = lua_tonumberx(Pointer, index, &isNumber);
        return isNumber != 0;
    }

    /// <summary>
    ///     Lua truthiness of the value at <paramref name="index" /> (<c>lua_toboolean</c>): everything except <c>nil</c>,
    ///     <c>false</c> and an absent value is <see langword="true" />.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ToBoolean(int index)
    {
        return lua_toboolean(Pointer, index) != 0;
    }

    /// <summary>
    ///     Reads the bytes of the string at <paramref name="index" /> without copying (<c>lua_tolstring</c>). Only a
    ///     real string qualifies: a number is not converted, because the conversion would rewrite the stack slot.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <param name="utf8">The bytes, embedded NULs included; empty when the value is not a string.</param>
    /// <returns><see langword="true" /> when the value is a string.</returns>
    /// <remarks>
    ///     Lifetime: the span points into Lua's memory and is valid only while the string stays on the stack (or is
    ///     otherwise reachable). Copy what must outlive the current frame. Never raises.
    /// </remarks>
    [LuaStackEffect(0)]
    public bool TryReadUtf8(int index, out ReadOnlySpan<byte> utf8)
    {
        if (lua_type(Pointer, index) != LUA_TSTRING)
        {
            utf8 = default;
            return false;
        }

        nuint length;
        var bytes = lua_tolstring(Pointer, index, &length);
        utf8 = new ReadOnlySpan<byte>(bytes, checked((int)length));
        return true;
    }

    /// <summary>
    ///     Copies the bytes of the string at <paramref name="index" /> into <paramref name="destination" />: the form that
    ///     lets a caller pop the value and keep the text, without allocating. The copy-out shape for a string result of
    ///     a generated wrapper (<c>Span&lt;byte&gt; destination, out int written</c>, copied before the stack is restored).
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <param name="destination">The buffer to copy into.</param>
    /// <param name="written">The number of bytes copied; 0 on failure.</param>
    /// <returns>
    ///     <see langword="false" /> when the value is not a string, or the string does not fit
    ///     <paramref name="destination" /> (nothing is copied then; <see cref="RawLength" /> gives the size needed).
    /// </returns>
    /// <remarks>Strict like <see cref="TryReadUtf8" />: a number is not converted. Two C API calls. Never raises.</remarks>
    [LuaStackEffect(0)]
    public bool TryCopyUtf8(int index, Span<byte> destination, out int written)
    {
        if (!TryReadUtf8(index, out var utf8) || !utf8.TryCopyTo(destination))
        {
            written = 0;
            return false;
        }

        written = utf8.Length;
        return true;
    }

    /// <summary>
    ///     Reads the string at <paramref name="index" /> as a managed <see cref="string" />, decoding UTF-8 (invalid
    ///     sequences become U+FFFD). Allocates the string: a convenience for cold paths, <see cref="TryReadUtf8" /> is the
    ///     primitive.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <param name="value">The decoded text; <see langword="null" /> when the value is not a string.</param>
    /// <returns><see langword="true" /> when the value is a string.</returns>
    [LuaStackEffect(0)]
    public bool TryReadString(int index, [NotNullWhen(true)] out string? value)
    {
        if (!TryReadUtf8(index, out var utf8))
        {
            value = null;
            return false;
        }

        value = Encoding.UTF8.GetString(utf8);
        return true;
    }

    /// <summary>
    ///     The pointer behind the value at <paramref name="index" /> (<c>lua_touserdata</c>): the block of a full userdata,
    ///     the value of a light userdata, zero for anything else.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <remarks>
    ///     A full userdata block belongs to Lua and lives as long as the userdata is reachable: read through it before
    ///     popping the value. Never raises.
    /// </remarks>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint ToUserdata(int index)
    {
        return (nint)lua_touserdata(Pointer, index);
    }

    /// <summary>
    ///     A generic identity for the value at <paramref name="index" /> (<c>lua_topointer</c>): the address of a table,
    ///     function, thread or userdata, usable for equality and diagnostics only; zero for other types.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint ToPointer(int index)
    {
        return (nint)lua_topointer(Pointer, index);
    }

    /// <summary>
    ///     The raw length of the value at <paramref name="index" /> (<c>lua_rawlen</c>): bytes of a string, border of a
    ///     table without <c>__len</c>, block size of a full userdata, 0 otherwise.
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <remarks>Never raises. <see cref="TryLength" /> is the metamethod-aware form.</remarks>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nuint RawLength(int index)
    {
        return lua_rawlen(Pointer, index);
    }
}
