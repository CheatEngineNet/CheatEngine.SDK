using System;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Base class for stable, domain-level failures from the typed Cheat Engine Engine surface. It deliberately wraps
///     lower-level failures instead of making Lua stack errors the public Engine error contract.
/// </summary>
/// <remarks>
///     Engine operations that document an expected negative result should normally expose a <c>Try*</c> or result form.
///     This hierarchy is for throwing APIs and for a binding failure that cannot be expressed by that operation's normal
///     result. A cause from the SDK can be retained as <see cref="Exception.InnerException" />, while the outer message
///     remains a stable Engine-level description.
/// </remarks>
public abstract class EngineException : Exception
{
	/// <summary>Initializes a domain error with its stable public message.</summary>
	/// <param name="message">The stable public message.</param>
	protected EngineException(string message)
		: base(RequireText(message, nameof(message)))
	{
	}

	/// <summary>Initializes a domain error with its stable public message and the underlying cause.</summary>
	/// <param name="message">The stable public message.</param>
	/// <param name="innerException">The lower-level cause, when one exists.</param>
	protected EngineException(string message, Exception? innerException)
		: base(RequireText(message, nameof(message)), innerException)
	{
	}

	/// <summary>Gets the stable category of this failure.</summary>
	public abstract EngineFailureKind Kind
	{
		get;
	}

	/// <summary>Validates and returns public contract text.</summary>
	/// <param name="value">The text to validate.</param>
	/// <param name="parameterName">The parameter name to report when validation fails.</param>
	/// <returns><paramref name="value" /> when it is nonempty.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is <see langword="null" /> or empty.</exception>
	protected static string RequireText(string value, string parameterName)
	{
		if (string.IsNullOrEmpty(value))
		{
			throw new ArgumentException("The value cannot be null or empty.", parameterName);
		}

		return value;
	}
}
