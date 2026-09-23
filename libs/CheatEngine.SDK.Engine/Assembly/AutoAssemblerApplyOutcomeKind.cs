namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>The factual category of one Auto Assembler activation attempt.</summary>
/// <remarks>
///     A category is never derived from Cheat Engine's error text. Cheat Engine gives no factual signal that separates a
///     syntax error, an unresolved symbol, an impossible allocation, a failed include or a failed DLL injection, so all of
///     them are <see cref="Rejected" /> (partially distinguishable by design); the opt-in host text is diagnostic only.
/// </remarks>
public enum AutoAssemblerApplyOutcomeKind
{
	/// <summary>No outcome was recorded: the value of <see langword="default" />.</summary>
	Unknown = 0,

	/// <summary>Cheat Engine applied the script, the target was unchanged afterwards, and the patch owner was published.</summary>
	Applied = 1,

	/// <summary>
	///     Cheat Engine applied the script, but the current target no longer matched the qualified incarnation afterwards.
	///     The patch owner is still published with its original incarnation; its release refuses another target.
	/// </summary>
	AppliedTargetChanged = 2,

	/// <summary>Cheat Engine returned <see langword="false" />. It does not prove that nothing changed.</summary>
	Rejected = 3,

	/// <summary>The <c>autoAssemble</c> global was absent or not a function; nothing was called.</summary>
	GlobalUnavailable = 4,

	/// <summary>The protected Lua call (or the global resolution) raised.</summary>
	ProtectedLuaFailure = 5,

	/// <summary>
	///     Cheat Engine returned a result outside the documented shapes, such as a non-boolean first result or success
	///     without a disable-info table.
	/// </summary>
	InvalidResult = 6,

	/// <summary>The current target could not be qualified as a process incarnation; nothing was called.</summary>
	TargetIdentityUnavailable = 7,

	/// <summary>
	///     Cheat Engine applied the script but the disable information could not be rooted, copied or handed to an owner;
	///     the SDK made its one target-qualified compensating disable, reported as the outcome's compensation.
	/// </summary>
	HandoffFailed = 8
}
