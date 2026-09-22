namespace CheatEngine.SDK.Engine.Targets;

/// <summary>The result of checking an owner-bound target incarnation against the current CE selection.</summary>
public enum TargetIdentityCheckKind : byte
{
	/// <summary>No validation result was recorded.</summary>
	Unspecified = 0,

	/// <summary>The current qualified selection matches the owner-bound incarnation.</summary>
	Current = 1,

	/// <summary>Cheat Engine no longer has a selected target.</summary>
	NoTargetSelected = 2,

	/// <summary>The selected PID is known but its incarnation could not be established.</summary>
	CurrentTargetUnqualified = 3,

	/// <summary>The selected PID differs from the owner-bound PID.</summary>
	TargetChanged = 4,

	/// <summary>The PID matches but its observed creation time differs from the owner-bound incarnation.</summary>
	ProcessReused = 5,

	/// <summary>The target-selection global was unavailable.</summary>
	GlobalUnavailable = 6,

	/// <summary>The protected Lua target observation failed.</summary>
	LuaFailure = 7,

	/// <summary>The target observation returned an unsupported value.</summary>
	InvalidResult = 8
}
