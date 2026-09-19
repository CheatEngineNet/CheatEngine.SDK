using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Calls;

/// <summary>
///     A failed protected Lua operation surfaced as a managed exception: the opt-in, throwing counterpart of the
///     <see cref="LuaStatus" /> that every <c>Try*</c> member returns. Thrown only between managed frames; it never
///     crosses into Lua or Cheat Engine.
/// </summary>
/// <remarks>
///     Construct through the cold helpers <see cref="ThrowFromStack" /> or <see cref="Throw(LuaError)" />, which do the
///     formatting off the hot path, or directly when re-raising a captured <see cref="LuaError" />.
/// </remarks>
public sealed class LuaException : Exception
{
    /// <summary>Creates an exception with a plain message and no Lua status.</summary>
    /// <param name="message">The message.</param>
    public LuaException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a plain message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public LuaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception from an extracted error.</summary>
    /// <param name="error">The status and text of the failure.</param>
    public LuaException(LuaError error)
        : base(error.Message)
    {
        Status = error.Status;
    }

    /// <summary>
    ///     Gets the Lua status of the failed operation; <see cref="LuaStatus.Ok" /> when the exception did not come from
    ///     a Lua status.
    /// </summary>
    public LuaStatus Status { get; }

    /// <summary>
    ///     Reads the error value on top of <paramref name="state" />'s stack (see <see cref="LuaError.FromStack" />) and
    ///     throws it. The value is left on the stack for the caller's frame. Cold and never inlined.
    /// </summary>
    /// <param name="state">The state the failed operation ran on.</param>
    /// <param name="status">The failure status.</param>
    /// <exception cref="LuaException">Always.</exception>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFromStack(LuaState state, LuaStatus status)
    {
        throw new LuaException(LuaError.FromStack(state, status));
    }

    /// <summary>Throws an extracted error. Cold and never inlined.</summary>
    /// <param name="error">The error.</param>
    /// <exception cref="LuaException">Always.</exception>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Throw(LuaError error)
    {
        throw new LuaException(error);
    }
}
