namespace CheatEngine.SDK.Engine.Targets;

/// <summary>The factual result of observing Cheat Engine's current target selection.</summary>
public enum TargetSelectionObservationStatus : byte
{
	/// <summary>No selection observation was recorded.</summary>
	Unspecified = 0,

	/// <summary>Cheat Engine reported a PID and the local process incarnation was observed.</summary>
	CurrentTargetQualified = 1,

	/// <summary>Cheat Engine reported that no target process is selected.</summary>
	NoTargetSelected = 2,

	/// <summary>Cheat Engine reported a PID, but its local process incarnation could not be established.</summary>
	CurrentTargetUnqualified = 3,

	/// <summary>The <c>getOpenedProcessID</c> global was absent or not callable.</summary>
	GlobalUnavailable = 4,

	/// <summary>The protected Lua observation failed.</summary>
	LuaFailure = 5,

	/// <summary>The observation result was not a supported integer PID.</summary>
	InvalidResult = 6
}
