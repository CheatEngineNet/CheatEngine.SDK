using System;

using CheatEngine.SDK.Engine.Errors;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     Represents a managed lease-publication failure after Cheat Engine registered a symbol and the SDK made its one
///     permitted compensation attempt.
/// </summary>
/// <remarks>
///     The managed publication failure remains <see cref="Exception.InnerException" />. <see cref="CleanupOutcome" />
///     separately records whether the single coordinator-qualified unregister was confirmed, could not begin, or
///     remains indeterminate; this exception does not provide a way to retry it.
/// </remarks>
public sealed class SymbolRegistrationHandoffException : EngineException
{
	/// <summary>
	///     Initializes a handoff failure with the result of the single compensation attempt and its managed publication
	///     cause.
	/// </summary>
	/// <param name="cleanupOutcome">The cleanup result observed after lease publication failed.</param>
	/// <param name="innerException">The managed failure that prevented lease publication.</param>
	public SymbolRegistrationHandoffException(SymbolRegistrationReleaseOutcome cleanupOutcome,
		Exception? innerException)
		: base(CreateMessage(cleanupOutcome), innerException)
	{
		CleanupOutcome = cleanupOutcome;
	}

	/// <summary>Gets the factual result of the one compensation attempt.</summary>
	public SymbolRegistrationReleaseOutcome CleanupOutcome
	{
		get;
	}

	/// <inheritdoc />
	public override EngineFailureKind Kind => EngineFailureKind.BindingFailure;

	private static string CreateMessage(SymbolRegistrationReleaseOutcome cleanupOutcome)
	{
		return
			"Cheat Engine registered a symbol before its managed cleanup lease could be published; compensation ended as " +
			cleanupOutcome.Kind + ".";
	}
}
