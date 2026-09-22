using System;

using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Represents a managed ownership publication failure after Cheat Engine already accepted an effect and the SDK made
///     its one permitted compensation attempt.
/// </summary>
/// <remarks>
///     The original handoff exception remains <see cref="Exception.InnerException" />. <see cref="CleanupOutcome" />
///     is deliberately separate from <see cref="Kind" />: it says whether the effect was compensated, safely refused,
///     or remains unconfirmed, without parsing a Lua error message or inviting a destructive retry.
/// </remarks>
public sealed class EngineResourceHandoffException : EngineException
{
	/// <summary>
	///     Initializes a handoff failure for <paramref name="operation" /> and records the factual result of its single
	///     compensation attempt.
	/// </summary>
	/// <param name="operation">The stable public identifier of the effectful operation.</param>
	/// <param name="cleanupOutcome">The compensation result observed after ownership publication failed.</param>
	/// <param name="innerException">The managed failure that prevented publication of the owner.</param>
	public EngineResourceHandoffException(string operation, TargetReleaseOutcome cleanupOutcome,
		Exception? innerException)
		: base(CreateMessage(operation, cleanupOutcome), innerException)
	{
		Operation = RequireText(operation, nameof(operation));
		CleanupOutcome = cleanupOutcome;
	}

	/// <summary>Gets the stable public identifier of the effect whose ownership could not be published.</summary>
	public string Operation
	{
		get;
	}

	/// <summary>Gets the factual outcome of the one compensation attempt.</summary>
	public TargetReleaseOutcome CleanupOutcome
	{
		get;
	}

	/// <inheritdoc />
	public override EngineFailureKind Kind => EngineFailureKind.BindingFailure;

	private static string CreateMessage(string operation, TargetReleaseOutcome cleanupOutcome)
	{
		return "The Engine operation '" + RequireText(operation, nameof(operation)) +
		       "' completed before its ownership could be published; compensation ended as " +
		       cleanupOutcome.Status + ".";
	}
}
