using System;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Represents an Engine binding whose required Lua global is absent or is not callable in the attached runtime.
/// </summary>
/// <remarks>
///     This differs from <see cref="EngineCapabilityUnavailableException" />: the Engine capability was selected as
///     available, but its binding could not resolve its required global. <see cref="Operation" /> is the stable public
///     operation identifier. The exact Lua global name remains an implementation detail of the binding.
/// </remarks>
public sealed class EngineGlobalUnavailableException : EngineException
{
	/// <summary>Initializes an exception with the standard message for <paramref name="operation" />.</summary>
	/// <param name="operation">The stable public Engine operation identifier.</param>
	public EngineGlobalUnavailableException(string operation)
		: this(operation, CreateDefaultMessage(operation), null)
	{
	}

	/// <summary>Initializes an exception with a caller-supplied stable message.</summary>
	/// <param name="operation">The stable public Engine operation identifier.</param>
	/// <param name="message">The stable public failure message.</param>
	public EngineGlobalUnavailableException(string operation, string message)
		: this(operation, message, null)
	{
	}

	/// <summary>Initializes an exception with a stable message and the lower-level cause.</summary>
	/// <param name="operation">The stable public Engine operation identifier.</param>
	/// <param name="message">The stable public failure message.</param>
	/// <param name="innerException">The lower-level cause, when one exists.</param>
	public EngineGlobalUnavailableException(string operation, string message, Exception? innerException)
		: base(message, innerException)
	{
		Operation = RequireText(operation, nameof(operation));
	}

	/// <summary>Gets the stable public identifier of the operation whose required global could not be resolved.</summary>
	public string Operation
	{
		get;
	}

	/// <inheritdoc />
	public override EngineFailureKind Kind => EngineFailureKind.GlobalUnavailable;

	private static string CreateDefaultMessage(string operation)
	{
		return "The required binding global for Cheat Engine operation '" +
			   RequireText(operation, nameof(operation)) + "' is unavailable.";
	}
}
