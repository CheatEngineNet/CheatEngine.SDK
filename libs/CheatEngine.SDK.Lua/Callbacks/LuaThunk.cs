using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Protected;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Text;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Callbacks;

/// <summary>
///     What a managed <c>lua_CFunction</c> (a thunk) calls: to read the state object a <see cref="LuaCallback" /> carries,
///     and to report a failure to Lua without raising. Every member is safe to call from inside an
///     <c>[UnmanagedCallersOnly]</c> method and never throws.
/// </summary>
/// <remarks>
///     <para>
///         <b>The error channel.</b> A thunk cannot call <c>lua_error</c>: the <c>longjmp</c> would unwind its managed
///         frame.
///         Instead it returns two values, the sentinel light userdata and a message, and returns 2. The Lua closure that
///         wraps every function registered through <see cref="LuaState.TryPushFunction" /> or
///         <see cref="LuaCallback" />
///         (<c>function(...) return check(f(...)) end</c>) sees the sentinel and calls <c>error(message, 2)</c> on the Lua
///         side, where unwinding is safe. To the Lua caller the failure is an ordinary, catchable error whose message is
///         the
///         one the thunk gave. A function pushed with <see cref="LuaState.PushUncheckedFunction" /> has no wrapper:
///         its
///         caller
///         receives the two values as results.
///     </para>
///     <para>
///         The shape of a thunk:
///         <code>
/// [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
/// static int Thunk(nint handle)
/// {
///     LuaState L = new(handle);
///     try
///     {
///         if (!LuaThunk.TryGetState(L, out Counter? counter)) return LuaThunk.Fail(L, "no state"u8);
///         L.PushInteger(++counter.Value);
///         return 1;
///     }
///     catch (Exception exception)
///     {
///         return LuaThunk.Fail(L, exception);
///     }
/// }
/// </code>
///     </para>
/// </remarks>
public static class LuaThunk
{
    /// <summary>
    ///     Number of values <see cref="Fail(LuaState,System.ReadOnlySpan{byte})" /> leaves on the stack, which
    ///     is also what the
    ///     thunk must return.
    /// </summary>
    public const int FailureResultCount = 2;

    /// <summary>
    ///     Reads the state object carried by the running closure's first upvalue, the one
    ///     <see cref="LuaCallback.TryCreate{TState}" />
    ///     installed. Only meaningful inside a thunk that Lua is currently running through such a closure: elsewhere the
    ///     upvalue pseudo-index is undefined.
    /// </summary>
    /// <typeparam name="TState">The expected type; a state of another type yields <see langword="false" />.</typeparam>
    /// <param name="state">The state the thunk received.</param>
    /// <param name="value">The state object.</param>
    /// <returns>
    ///     <see langword="false" /> when the closure has no state (pushed without one, or released) or the state is not a
    ///     <typeparamref name="TState" />.
    /// </returns>
    /// <remarks>One C API call and one type check; allocates nothing.</remarks>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool TryGetState<TState>(LuaState state, [NotNullWhen(true)] out TState? value)
        where TState : class
    {
        // Release must not free the handle between reading its address and rooting its target.
        lock (LuaCallbackRegistry.Gate)
        {
            var upvalue = lua_touserdata(state.Pointer, lua_upvalueindex(1));
            if (upvalue is null)
            {
                value = null;
                return false;
            }

            value = GCHandle<object>.FromIntPtr((nint)upvalue).Target as TState;
            return value is not null;
        }
    }

    /// <summary>
    ///     Reports a failure to Lua: pushes the sentinel and <paramref name="message" /> and returns
    ///     <see cref="FailureResultCount" />, for the thunk to return in turn. Allocates nothing on the managed side.
    /// </summary>
    /// <param name="state">The state the thunk received.</param>
    /// <param name="message">The error message, UTF-8.</param>
    /// <returns>2.</returns>
    /// <remarks>
    ///     The message allocation runs through the native protection bridge. If Lua cannot allocate the message, the
    ///     protected Lua error value becomes the second result. A bridge failure falls back to <c>nil</c>, so this method
    ///     never lets a Lua <c>longjmp</c> or managed exception escape the thunk.
    /// </remarks>
    [LuaStackEffect(FailureResultCount)]
    public static unsafe int Fail(LuaState state, ReadOnlySpan<byte> message)
    {
        LuaHelpers.PushSentinel(state.Pointer);
        try
        {
            _ = state.TryPushString(message);
        }
        catch (Exception)
        {
            state.PushNil();
        }

        return FailureResultCount;
    }

    /// <summary>
    ///     Reports a failure with a UTF-16 message (a <see cref="string" /> converts implicitly); see
    ///     <see cref="Fail(LuaState,System.ReadOnlySpan{byte})" />.
    /// </summary>
    /// <param name="state">The state the thunk received.</param>
    /// <param name="message">The error message.</param>
    /// <returns>2.</returns>
    [LuaStackEffect(FailureResultCount)]
    public static unsafe int Fail(LuaState state, ReadOnlySpan<char> message)
    {
        try
        {
            Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
            using var utf8 = Utf8Scratch.Encode(message, scratch);
            return Fail(state, utf8.Bytes);
        }
        catch (Exception)
        {
            LuaHelpers.PushSentinel(state.Pointer);
            state.PushNil();
            return FailureResultCount;
        }
    }

    /// <summary>
    ///     Reports an argument of the wrong kind the way Lua's own <c>luaL_check*</c> functions do, naming the type
    ///     actually received: <c>bad argument #2 (integer expected, got nil)</c>. Built in a stack buffer; allocates
    ///     nothing on the managed side. See <see cref="Fail(LuaState,System.ReadOnlySpan{byte})" />.
    /// </summary>
    /// <param name="state">The state the thunk received.</param>
    /// <param name="argument">The 1-based position of the argument, which is also its stack index.</param>
    /// <param name="expected">What the thunk expected (<c>"integer"u8</c>, <c>"string"u8</c>, ...); truncated beyond 64 bytes.</param>
    /// <returns>2.</returns>
    [LuaStackEffect(FailureResultCount)]
    [SkipLocalsInit] // Every byte read from the buffer is written first; zeroing it would be dead stores.
    public static int FailBadArgument(LuaState state, int argument, ReadOnlySpan<byte> expected)
    {
        // "bad argument #" + digits + " (" + expected + " expected, got " + type name + ")".
        Span<byte> message = stackalloc byte[128];
        var length = 0;
        Append(message, ref length, "bad argument #"u8);
        if (!argument.TryFormat(message[length..], out var digits, default, CultureInfo.InvariantCulture)) digits = 0;

        length += digits;
        Append(message, ref length, " ("u8);
        Append(message, ref length, expected.Length > 64 ? expected[..64] : expected);
        Append(message, ref length, " expected, got "u8);
        Append(message, ref length, state.TypeName(argument));
        Append(message, ref length, ")"u8);
        return Fail(state, message[..length]);
    }

    private static void Append(Span<byte> buffer, ref int length, ReadOnlySpan<byte> text)
    {
        // The pieces are sized so that this never truncates; the guard keeps the method safe if it ever changes.
        var count = Math.Min(text.Length, buffer.Length - length);
        text[..count].CopyTo(buffer[length..]);
        length += count;
    }

    /// <summary>
    ///     Reports an exception caught by the thunk as <c>TypeName: Message</c>; see
    ///     <see cref="Fail(LuaState,System.ReadOnlySpan{byte})" />.
    ///     Never throws itself: if formatting the exception fails, a fixed message is used.
    /// </summary>
    /// <param name="state">The state the thunk received.</param>
    /// <param name="exception">The caught exception; <see langword="null" /> is reported as an unknown failure.</param>
    /// <returns>2.</returns>
    [LuaStackEffect(FailureResultCount)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Fail(LuaState state, Exception? exception)
    {
        string message;
        try
        {
            message = exception is null
                ? "managed callback failed"
                : exception.GetType().FullName + ": " + exception.Message;
        }
        catch (Exception)
        {
            message = "managed callback failed (the exception could not be described)";
        }

        return Fail(state, message.AsSpan());
    }
}
