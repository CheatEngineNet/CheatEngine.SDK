using System;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Represents a failure of a protected Lua operation performed by an Engine binding.
/// </summary>
/// <remarks>
///     The public message identifies only the Engine operation and <see cref="Status" />. In particular, it does not
///     copy a Lua error object or its stack text into the stable Engine error surface. If a lower-level SDK exception is
///     useful for diagnostics, retain it as <see cref="Exception.InnerException" /> and consumers can inspect it explicitly.
/// </remarks>
public sealed class EngineLuaException : EngineException
{
    /// <summary>Initializes an exception with the standard public message for the protected Lua failure.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="status">The non-success status returned by the protected Lua operation.</param>
    /// <exception cref="ArgumentException"><paramref name="status" /> represents a successful Lua operation.</exception>
    public EngineLuaException(string operation, LuaStatus status)
        : this(operation, status, CreateDefaultMessage(operation, status), null)
    {
    }

    /// <summary>Initializes an exception with a caller-supplied stable public message.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="status">The non-success status returned by the protected Lua operation.</param>
    /// <param name="message">The stable public failure message.</param>
    /// <exception cref="ArgumentException"><paramref name="status" /> represents a successful Lua operation.</exception>
    public EngineLuaException(string operation, LuaStatus status, string message)
        : this(operation, status, message, null)
    {
    }

    /// <summary>Initializes an exception with a stable public message and the lower-level cause.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="status">The non-success status returned by the protected Lua operation.</param>
    /// <param name="message">The stable public failure message.</param>
    /// <param name="innerException">The lower-level cause, when one exists.</param>
    /// <exception cref="ArgumentException"><paramref name="status" /> represents a successful Lua operation.</exception>
    public EngineLuaException(string operation, LuaStatus status, string message, Exception? innerException)
        : base(message, innerException)
    {
        if (status.IsOk) throw new ArgumentException("A successful Lua status cannot describe a failure.", nameof(status));

        Operation = RequireText(operation, nameof(operation));
        Status = status;
    }

    /// <summary>Gets the stable public identifier of the Engine operation that ran Lua.</summary>
    public string Operation { get; }

    /// <summary>Gets the non-success protected Lua status.</summary>
    public LuaStatus Status { get; }

    /// <inheritdoc />
    public override EngineFailureKind Kind => EngineFailureKind.ProtectedLuaFailure;

    private static string CreateDefaultMessage(string operation, LuaStatus status)
    {
        if (status.IsOk) throw new ArgumentException("A successful Lua status cannot describe a failure.", nameof(status));

        return "The protected Lua operation '" + RequireText(operation, nameof(operation)) + "' failed with status " +
               status + ".";
    }
}
