using System;

using CheatEngine.SDK.Engine.Errors;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     Represents a managed lease-construction failure after Cheat Engine registered a symbol list and the SDK made its
///     one permitted compensating <c>unregister()</c>.
/// </summary>
/// <remarks>
///     The managed failure remains <see cref="Exception.InnerException" />. <see cref="CleanupOutcome" /> records whether
///     the compensation was confirmed (the caller keeps its <c>Owned&lt;SymbolList&gt;</c>, now unregistered), could not
///     begin, or remains indeterminate (the list was then abandoned without destroy, because destroying a possibly
///     registered list could leave a dangling entry in Cheat Engine's symbol handler). It never offers a retry.
/// </remarks>
public sealed class SymbolListRegistrationHandoffException : EngineException
{
	/// <summary>
	///     Initializes a handoff failure with the result of the single compensation attempt and its managed cause.
	/// </summary>
	/// <param name="cleanupOutcome">The compensation result observed after lease construction failed.</param>
	/// <param name="innerException">The managed failure that prevented lease construction.</param>
	public SymbolListRegistrationHandoffException(SymbolListRegistrationReleaseOutcome cleanupOutcome,
		Exception? innerException)
		: base(CreateMessage(cleanupOutcome), innerException)
	{
		CleanupOutcome = cleanupOutcome;
	}

	/// <summary>Gets the factual result of the one compensation attempt.</summary>
	public SymbolListRegistrationReleaseOutcome CleanupOutcome
	{
		get;
	}

	/// <inheritdoc />
	public override EngineFailureKind Kind => EngineFailureKind.BindingFailure;

	private static string CreateMessage(SymbolListRegistrationReleaseOutcome cleanupOutcome)
	{
		return
			"Cheat Engine registered a symbol list before its managed lease could be published; compensation ended as " +
			cleanupOutcome.UnregisterKind + ".";
	}
}
