using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>Represents a target-bound operation refused because the original target cannot be verified as current.</summary>
public sealed class EngineTargetIdentityException : EngineException
{
	/// <summary>Initializes a target-identity refusal for a stable Engine operation identifier.</summary>
	/// <param name="operation">The stable public operation identifier.</param>
	/// <param name="check">The factual target validation result.</param>
	public EngineTargetIdentityException(string operation, TargetIdentityCheck check)
		: base(CreateMessage(operation, check))
	{
		Operation = RequireText(operation, nameof(operation));
		Check = check;
	}

	/// <summary>Gets the stable public operation identifier.</summary>
	public string Operation
	{
		get;
	}

	/// <summary>Gets the target validation result that caused the safe refusal.</summary>
	public TargetIdentityCheck Check
	{
		get;
	}

	/// <inheritdoc />
	public override EngineFailureKind Kind => Check.Kind is TargetIdentityCheckKind.TargetChanged or
		TargetIdentityCheckKind.ProcessReused
		? EngineFailureKind.TargetIdentityMismatch
		: EngineFailureKind.TargetIdentityUnavailable;

	private static string CreateMessage(string operation, TargetIdentityCheck check)
	{
		return "The Engine operation '" + RequireText(operation, nameof(operation)) +
		       "' was refused because its original target is not verified as current (" + check.Kind + ").";
	}
}
