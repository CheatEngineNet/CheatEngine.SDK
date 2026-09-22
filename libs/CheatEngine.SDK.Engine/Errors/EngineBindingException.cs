using System;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Represents an Engine binding that cannot uphold the contract declared by its generated or handwritten
///     specification.
/// </summary>
/// <remarks>
///     This is a binding/specification compatibility failure, not an expected result reported by Cheat Engine and not a
///     protected Lua failure. <see cref="Binding" /> is a stable public binding identifier, never an additional-file
///     path or an implementation detail of the source generator. A malformed EngineApi specification is instead a
///     build-time <c>CESDK3xxx</c> diagnostic and must not be converted into this runtime exception.
/// </remarks>
public sealed class EngineBindingException : EngineException
{
	/// <summary>Initializes an exception with the standard message for <paramref name="binding" />.</summary>
	/// <param name="binding">The stable public binding identifier.</param>
	public EngineBindingException(string binding)
		: this(binding, CreateDefaultMessage(binding), null)
	{
	}

	/// <summary>Initializes an exception with a caller-supplied stable message.</summary>
	/// <param name="binding">The stable public binding identifier.</param>
	/// <param name="message">The stable public failure message.</param>
	public EngineBindingException(string binding, string message)
		: this(binding, message, null)
	{
	}

	/// <summary>Initializes an exception with a stable message and the lower-level cause.</summary>
	/// <param name="binding">The stable public binding identifier.</param>
	/// <param name="message">The stable public failure message.</param>
	/// <param name="innerException">The lower-level cause, when one exists.</param>
	public EngineBindingException(string binding, string message, Exception? innerException)
		: base(message, innerException)
	{
		Binding = RequireText(binding, nameof(binding));
	}

	/// <summary>Gets the stable public identifier of the invalid binding.</summary>
	public string Binding
	{
		get;
	}

	/// <inheritdoc />
	public override EngineFailureKind Kind => EngineFailureKind.BindingFailure;

	private static string CreateDefaultMessage(string binding)
	{
		return "The Cheat Engine binding '" + RequireText(binding, nameof(binding)) + "' cannot uphold its contract.";
	}
}
