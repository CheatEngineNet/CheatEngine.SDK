using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Calls;

/// <summary>
///     The error of a failed protected operation, extracted from the Lua stack into managed memory without throwing: the
///     <see cref="LuaStatus" /> and the error value rendered as text.
/// </summary>
/// <remarks>
///     Building one allocates the message string, so it belongs on failure paths only; a <c>Try*</c> method that only
///     needs to report failure returns the status and leaves the error value for the frame to discard.
/// </remarks>
/// <remarks>Creates an error from its parts.</remarks>
/// <param name="status">The failure status.</param>
/// <param name="message">The error text; never <see langword="null" />.</param>
public readonly struct LuaError(LuaStatus status, string message) : IEquatable<LuaError>
{
    /// <summary>Gets the status of the failed operation.</summary>
    public LuaStatus Status { get; } = status;

    /// <summary>
    ///     Gets the error text: the string error value, a number converted to text, or a description of the value's type
    ///     when the error value is neither (<c>(error object is a table value)</c>, in the style of <c>lua.c</c>).
    /// </summary>
    public string Message { get; } = message ?? string.Empty;

    /// <summary>
    ///     Reads the error value on top of <paramref name="state" />'s stack, which is where every failed protected
    ///     operation leaves it. The value stays on the stack (stack effect 0); the caller's frame discards it.
    /// </summary>
    /// <param name="state">The state the failed operation ran on.</param>
    /// <param name="status">The status that operation returned.</param>
    /// <returns>
    ///     The extracted error. When the stack is empty the message says so instead of reading a slot that does not
    ///     exist.
    /// </returns>
    /// <remarks>
    ///     Never runs Lua code: a non-string, non-number error value is described by its type instead of being passed to
    ///     <c>tostring</c>, because a <c>__tostring</c> metamethod could raise. Numbers are formatted in managed code,
    ///     avoiding the allocating numeric conversion performed by <c>lua_tolstring</c>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static unsafe LuaError FromStack(LuaState state, LuaStatus status)
    {
        if (state.IsNull || state.Top == 0) return new LuaError(status, "(no error value on the stack)");

        switch (state.TypeOf(-1))
        {
            case LuaType.String:
                nuint length;
                var bytes = LuaApi.lua_tolstring(state.Pointer, -1, &length);
                return new LuaError(status, Encoding.UTF8.GetString(bytes, checked((int)length)));
            case LuaType.Number:
                return new LuaError(status, LuaApi.lua_isinteger(state.Pointer, -1) != 0
                    ? LuaApi.lua_tointegerx(state.Pointer, -1, null).ToString(CultureInfo.InvariantCulture)
                    : LuaApi.lua_tonumberx(state.Pointer, -1, null).ToString("G", CultureInfo.InvariantCulture));
            case LuaType.Nil:
                return new LuaError(status, "(error object is a nil value)");
            default:
                var typeName = Encoding.UTF8.GetString(
                    MemoryMarshal.CreateReadOnlySpanFromNullTerminated(LuaApi.lua_typename(state.Pointer,
                        (int)state.TypeOf(-1))));
                return new LuaError(status,
                    string.Create(CultureInfo.InvariantCulture, $"(error object is a {typeName} value)"));
        }
    }

    /// <summary>Compares status and message.</summary>
    /// <param name="left">First error.</param>
    /// <param name="right">Second error.</param>
    public static bool operator ==(LuaError left, LuaError right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares status and message.</summary>
    /// <param name="left">First error.</param>
    /// <param name="right">Second error.</param>
    public static bool operator !=(LuaError left, LuaError right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(LuaError other)
    {
        return Status == other.Status && string.Equals(Message, other.Message, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LuaError other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Status, Message);
    }

    /// <summary><c>LUA_ERRRUN: message</c>.</summary>
    public override string ToString()
    {
        return Status + ": " + Message;
    }
}
