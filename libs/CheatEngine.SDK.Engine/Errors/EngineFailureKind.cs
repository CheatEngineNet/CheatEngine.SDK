namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Categorizes a failure reported by the typed Cheat Engine Engine surface. The numeric values are stable so callers
///     can classify an <see cref="EngineException" /> without parsing an exception message.
/// </summary>
public enum EngineFailureKind
{
	/// <summary>
	///     Cheat Engine completed a call but reported the expected operation failure defined by that operation's
	///     contract, such as an unreadable target address.
	/// </summary>
	ExpectedOperationFailure = 0,

	/// <summary>
	///     A generated or handwritten Engine binding could not resolve its required Lua global as a callable function.
	/// </summary>
	GlobalUnavailable = 1,

	/// <summary>
	///     A required public Engine capability is not available in the attached Cheat Engine runtime.
	/// </summary>
	CapabilityUnavailable = 2,

	/// <summary>
	///     A protected Lua operation failed before the binding could obtain its declared result.
	/// </summary>
	ProtectedLuaFailure = 3,

	/// <summary>
	///     A generated or handwritten Engine binding does not match its declared contract.
	/// </summary>
	BindingFailure = 4,

	/// <summary>
	///     A value crossing the Engine/Lua boundary could not be marshalled according to its declared contract.
	/// </summary>
	MarshallingFailure = 5,

	/// <summary>
	///     A target-bound operation could not establish the current target's required identity facts.
	/// </summary>
	TargetIdentityUnavailable = 6,

	/// <summary>
	///     A target-bound owner no longer matches the target currently selected by Cheat Engine.
	/// </summary>
	TargetIdentityMismatch = 7
}
