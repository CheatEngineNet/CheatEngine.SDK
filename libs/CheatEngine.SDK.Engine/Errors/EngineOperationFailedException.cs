using System;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Represents an expected negative result reported by a Cheat Engine operation when its throwing form is used.
/// </summary>
/// <remarks>
///     Prefer the operation's <c>Try*</c> or result-returning form when the caller expects this outcome. The
///     <see cref="Operation" /> is a stable public Engine operation identifier, not a raw Lua global name.
/// </remarks>
public sealed class EngineOperationFailedException : EngineException
{
    /// <summary>Initializes an exception with the standard message for <paramref name="operation" />.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    public EngineOperationFailedException(string operation)
        : this(operation, CreateDefaultMessage(operation), null)
    {
    }

    /// <summary>Initializes an exception with a caller-supplied stable message.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="message">The stable public failure message.</param>
    public EngineOperationFailedException(string operation, string message)
        : this(operation, message, null)
    {
    }

    /// <summary>Initializes an exception with a stable message and the lower-level cause.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="message">The stable public failure message.</param>
    /// <param name="innerException">The lower-level cause, when one exists.</param>
    public EngineOperationFailedException(string operation, string message, Exception? innerException)
        : base(message, innerException)
    {
        Operation = RequireText(operation, nameof(operation));
    }

    /// <summary>Gets the stable public identifier of the operation that reported failure.</summary>
    public string Operation { get; }

    /// <inheritdoc />
    public override EngineFailureKind Kind => EngineFailureKind.ExpectedOperationFailure;

    private static string CreateDefaultMessage(string operation)
    {
        return "The Cheat Engine operation '" + RequireText(operation, nameof(operation)) + "' reported failure.";
    }
}
