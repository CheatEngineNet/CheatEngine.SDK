namespace CheatEngine.SDK.Engine.Processes;

/// <summary>The factual result of observing or selecting Cheat Engine's target process.</summary>
/// <remarks>
///     The numeric values are explicit and stable. <see cref="Unknown" /> is the zero value, so an unassigned status
///     (for example <c>default(ProcessOperationStatus)</c>) never reads as <see cref="Success" />.
/// </remarks>
public enum ProcessOperationStatusKind
{
	/// <summary>No operation result was recorded; never a success.</summary>
	Unknown = 0,

	/// <summary>The requested process operation completed and its declared facts were observed.</summary>
	Success = 1,

	/// <summary>Cheat Engine reported that no process is selected.</summary>
	TargetNotAttached = 2,

	/// <summary>
	///     An explicit selection call returned normally, but the immediately observed process was absent or did not match
	///     the requested identifier.
	/// </summary>
	SelectionNotConfirmed = 3,

	/// <summary>A required Lua global was absent or was not callable.</summary>
	GlobalUnavailable = 4,

	/// <summary>A protected Lua lookup or invocation failed.</summary>
	ProtectedLuaFailure = 5,

	/// <summary>The host returned a value outside the documented process-observation shape.</summary>
	InvalidResult = 6,

	/// <summary>
	///     Cheat Engine reported a different selected process identifier in the two reads that bracket the observation,
	///     so the facts read between them cannot be attributed to one target.
	/// </summary>
	TargetChanged = 7,

	/// <summary>
	///     Cheat Engine reported the file-as-process sentinel identifier (4294967295): the selected target is a file
	///     opened as a process, not an operating-system process, and no target fact is read for it.
	/// </summary>
	FileAsProcessTarget = 8
}
