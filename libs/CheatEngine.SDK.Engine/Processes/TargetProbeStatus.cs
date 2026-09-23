namespace CheatEngine.SDK.Engine.Processes;

/// <summary>The internal result category of one <see cref="TargetArchitectureProbe" /> observation.</summary>
/// <remarks>Each public operation maps these categories to its own status type; none is exposed as-is.</remarks>
internal enum TargetProbeStatus : byte
{
	/// <summary>No probe result was recorded.</summary>
	Unknown = 0,

	/// <summary>A positive process identifier was read twice with the same value and every required fact was read.</summary>
	Success = 1,

	/// <summary><c>getOpenedProcessID</c> returned 0: no target is selected, and no target fact was read.</summary>
	NoTargetSelected = 2,

	/// <summary>
	///     <c>getOpenedProcessID</c> returned the file-as-process sentinel 4294967295, and no target fact was read.
	/// </summary>
	FileAsProcess = 3,

	/// <summary>
	///     <c>getOpenedProcessID</c> returned a value that is not a Lua integer (a float, even an integral one, is refused)
	///     or an integer outside the supported identifier range.
	/// </summary>
	InvalidProcessId = 4,

	/// <summary>A required global was absent or not callable.</summary>
	GlobalUnavailable = 5,

	/// <summary>Resolving or calling a global raised; the protected <c>LuaStatus</c> is kept.</summary>
	LuaFailure = 6,

	/// <summary>A fact global returned a value of the wrong Lua type or outside its supported range.</summary>
	InvalidResult = 7,

	/// <summary>The two process-identifier reads that bracket the facts disagree.</summary>
	TargetChanged = 8
}
