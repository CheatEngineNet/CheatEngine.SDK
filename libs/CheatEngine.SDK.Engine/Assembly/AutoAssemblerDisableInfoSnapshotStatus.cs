namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>How completely a disable-info snapshot copied Cheat Engine's disable-info table.</summary>
/// <remarks>
///     A truncated or malformed snapshot never fails an activation: the rooted disable-info table, not the snapshot,
///     stays the disable authority.
/// </remarks>
public enum AutoAssemblerDisableInfoSnapshotStatus
{
	/// <summary>No snapshot status was recorded: the value of <see langword="default" />.</summary>
	Unknown = 0,

	/// <summary>Every projected section was copied.</summary>
	Complete = 1,

	/// <summary>An entry or name limit was reached; the copied entries are exact, some were not copied.</summary>
	Truncated = 2,

	/// <summary>
	///     At least one entry or section had an unexpected shape and was skipped (this status wins over
	///     <see cref="Truncated" />).
	/// </summary>
	Malformed = 3
}
