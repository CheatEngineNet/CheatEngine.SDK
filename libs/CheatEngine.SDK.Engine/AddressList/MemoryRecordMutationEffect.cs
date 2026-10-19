namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>States how far a table-record mutation progressed at the Cheat Engine boundary.</summary>
public enum MemoryRecordMutationEffect
{
	/// <summary>No CE mutation was started.</summary>
	NotAttempted,

	/// <summary>The CE mutation completed successfully, including any required postcondition read-back.</summary>
	Completed,

	/// <summary>
	///     The CE mutation started but returned a protected failure or left its postcondition unconfirmed, so its final
	///     host state is unknown.
	/// </summary>
	Indeterminate
}
