namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Describes the factual result of a bounded instruction operation.</summary>
/// <remarks>
///     <para>
///         The values intentionally distinguish a caller-supplied profile or destination failure from a missing CE
///         primitive, a protected Lua failure, and a CE result that does not match the pinned contract. No value turns
///         a successful PID-and-ISA observation into a process-incarnation guarantee, a target-selection lock, or live
///         host qualification.
///     </para>
///     <para>
///         The numeric values are explicit and stable. <see cref="Unknown" /> is the zero value, so an unassigned status
///         (for example <c>default(InstructionOperationStatus)</c>) never reads as <see cref="Success" />.
///     </para>
/// </remarks>
public enum InstructionOperationStatus
{
	/// <summary>No operation result was recorded; never a success.</summary>
	Unknown = 0,

	/// <summary>The operation completed and copied its complete result.</summary>
	Success = 1,

	/// <summary>
	///     The instruction profile does not name a supported x86, x64, ARM32, or ARM64 architecture with its matching
	///     width, or Cheat Engine reported contradictory target ISA-family facts (both the x86 and the ARM family, or
	///     neither).
	/// </summary>
	InvalidProfile = 2,

	/// <summary>The input or returned address has bits outside the caller-declared profile width.</summary>
	AddressExceedsProfileWidth = 3,

	/// <summary>
	///     Cheat Engine did not report a positive selected target process identifier when the profile was observed, or
	///     the supplied profile names no target. An operation whose re-check finds no target reports
	///     <see cref="TargetChanged" /> instead.
	/// </summary>
	TargetNotSelected = 4,

	/// <summary>
	///     Cheat Engine reported a different selected target before and after an instruction profile observation, or an
	///     operation's re-check found a selection other than the profiled process: another identifier, no target, or the
	///     file-as-process sentinel. After the host call this is an uncertainty status: the call ran, but its result is
	///     not attributed to the profiled target and nothing is copied.
	/// </summary>
	TargetChanged = 5,

	/// <summary>The caller-owned destination cannot hold the complete byte result; no byte was written.</summary>
	DestinationTooSmall = 6,

	/// <summary>The raw UTF-8 line exceeds the explicit maximum before the SDK decodes or publishes text.</summary>
	OutputTooLong = 7,

	/// <summary>Cheat Engine rejected an otherwise well-formed assembly instruction.</summary>
	InstructionRejected = 8,

	/// <summary>The required Cheat Engine Lua global was missing or was not callable.</summary>
	GlobalUnavailable = 9,

	/// <summary>A protected Lua lookup, argument push, or call failed.</summary>
	LuaFailure = 10,

	/// <summary>Cheat Engine returned a value whose type, table elements, or instruction length violated the contract.</summary>
	InvalidResult = 11,

	/// <summary>
	///     The selected target is not an operating-system process: Cheat Engine reported the file-as-process sentinel
	///     identifier (4294967295). A file opened as a process has no instruction profile in the SDK.
	/// </summary>
	UnsupportedTargetBackend = 12
}
