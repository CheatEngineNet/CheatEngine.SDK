namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>The factual category of one <c>autoAssembleCheck</c> syntax check.</summary>
/// <remarks>A category is never derived from Cheat Engine's error text.</remarks>
public enum AutoAssemblerCheckOutcomeKind
{
	/// <summary>No outcome was recorded: the value of <see langword="default" />.</summary>
	Unknown = 0,

	/// <summary>Cheat Engine accepted the script section. This does not prove that an activation will succeed.</summary>
	Accepted = 1,

	/// <summary>Cheat Engine reported a problem in the script section.</summary>
	Rejected = 2,

	/// <summary>The <c>autoAssembleCheck</c> global was absent or not a function; nothing was called.</summary>
	GlobalUnavailable = 3,

	/// <summary>The protected Lua call (or the global resolution) raised.</summary>
	ProtectedLuaFailure = 4,

	/// <summary>Cheat Engine returned a result outside the documented shape (a non-boolean first result).</summary>
	InvalidResult = 5
}
