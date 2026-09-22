namespace CheatEngine.SDK.Engine.Processes;

/// <summary>The factual result of observing or selecting Cheat Engine's target process.</summary>
public enum ProcessOperationStatusKind
{
	/// <summary>The requested process operation completed and its declared facts were observed.</summary>
	Success,

	/// <summary>Cheat Engine reported that no process is selected.</summary>
	TargetNotAttached,

	/// <summary>
	///     An explicit selection call returned normally, but the immediately observed process was absent or did not match
	///     the requested identifier.
	/// </summary>
	SelectionNotConfirmed,

	/// <summary>A required Lua global was absent or was not callable.</summary>
	GlobalUnavailable,

	/// <summary>A protected Lua lookup or invocation failed.</summary>
	ProtectedLuaFailure,

	/// <summary>The host returned a value outside the documented process-observation shape.</summary>
	InvalidResult
}
