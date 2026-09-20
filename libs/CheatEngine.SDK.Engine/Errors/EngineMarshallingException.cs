using System;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Represents a value that could not be marshalled across the Engine/Lua boundary according to its declared
///     contract.
/// </summary>
/// <remarks>
///     Use <see cref="Direction" /> to distinguish invalid input from a successful call that returned an invalid result.
///     <see cref="Expected" /> and <see cref="Actual" /> are stable, binding-provided type descriptions; they are not
///     Lua error objects or stack traces.
/// </remarks>
public sealed class EngineMarshallingException : EngineException
{
    /// <summary>Initializes an exception with the standard message for the marshalling mismatch.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="direction">Whether the mismatch occurred for an argument or a result.</param>
    /// <param name="expected">The type or shape the binding declared.</param>
    /// <param name="actual">The type or shape the binding observed.</param>
    public EngineMarshallingException(string operation, EngineMarshallingDirection direction, string expected,
        string actual)
        : this(operation, direction, expected, actual, CreateDefaultMessage(operation, direction, expected, actual),
            innerException: null)
    {
    }

    /// <summary>Initializes an exception with a caller-supplied stable message.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="direction">Whether the mismatch occurred for an argument or a result.</param>
    /// <param name="expected">The type or shape the binding declared.</param>
    /// <param name="actual">The type or shape the binding observed.</param>
    /// <param name="message">The stable public failure message.</param>
    public EngineMarshallingException(string operation, EngineMarshallingDirection direction, string expected,
        string actual, string message)
        : this(operation, direction, expected, actual, message, innerException: null)
    {
    }

    /// <summary>Initializes an exception with a stable message and the lower-level cause.</summary>
    /// <param name="operation">The stable public Engine operation identifier.</param>
    /// <param name="direction">Whether the mismatch occurred for an argument or a result.</param>
    /// <param name="expected">The type or shape the binding declared.</param>
    /// <param name="actual">The type or shape the binding observed.</param>
    /// <param name="message">The stable public failure message.</param>
    /// <param name="innerException">The lower-level cause, when one exists.</param>
    public EngineMarshallingException(string operation, EngineMarshallingDirection direction, string expected,
        string actual, string message, Exception? innerException)
        : base(message, innerException)
    {
        Operation = RequireText(operation, nameof(operation));
        Direction = ValidateDirection(direction);
        Expected = RequireText(expected, nameof(expected));
        Actual = RequireText(actual, nameof(actual));
    }

    /// <summary>Gets the stable public identifier of the Engine operation.</summary>
    public string Operation { get; }

    /// <summary>Gets whether the mismatch happened while writing an argument or reading a result.</summary>
    public EngineMarshallingDirection Direction { get; }

    /// <summary>Gets the type or shape the binding declared.</summary>
    public string Expected { get; }

    /// <summary>Gets the type or shape the binding observed.</summary>
    public string Actual { get; }

    /// <inheritdoc />
    public override EngineFailureKind Kind => EngineFailureKind.MarshallingFailure;

    private static string CreateDefaultMessage(string operation, EngineMarshallingDirection direction, string expected,
        string actual)
    {
        var directionText = ValidateDirection(direction) == EngineMarshallingDirection.Argument ? "argument" : "result";
        return "The " + directionText + " of Cheat Engine operation '" +
               RequireText(operation, nameof(operation)) + "' could not be marshalled: expected " +
               RequireText(expected, nameof(expected)) + ", observed " + RequireText(actual, nameof(actual)) + ".";
    }

    private static EngineMarshallingDirection ValidateDirection(EngineMarshallingDirection direction)
    {
        if (direction != EngineMarshallingDirection.Argument && direction != EngineMarshallingDirection.Result)
            throw new ArgumentOutOfRangeException(nameof(direction), direction,
                "The marshalling direction is not defined.");

        return direction;
    }
}
