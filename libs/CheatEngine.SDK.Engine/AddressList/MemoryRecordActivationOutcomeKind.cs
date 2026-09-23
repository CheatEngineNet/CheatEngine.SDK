namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>The factual result of one <see cref="AddressListMutations.SetActive" /> command.</summary>
/// <remarks>
///     The SDK never retries an activation. A third-party <c>OnActivationFailure</c> handler that asks Cheat Engine to
///     retry can make Cheat Engine loop inside the one setter call; the SDK cannot prevent that and does not add a loop of
///     its own.
/// </remarks>
public enum MemoryRecordActivationOutcomeKind
{
	/// <summary>No outcome was recorded: the value of <see langword="default" />.</summary>
	Unknown = 0,

	/// <summary>The setter ran and the record reads back the requested state, with no asynchronous processing pending.</summary>
	Applied = 1,

	/// <summary>The record was already in the requested state; the setter was not called.</summary>
	Unchanged = 2,

	/// <summary>
	///     The setter ran but the record still reads the previous state: Cheat Engine or a record callback refused (an
	///     <c>OnActivate</c> returning false, a failed <c>[ENABLE]</c>). The refusal may have applied part of its effects.
	/// </summary>
	RefusedByHost = 3,

	/// <summary>
	///     The setter ran and the asynchronous activation of the record is still being processed; the final state is not
	///     known yet.
	/// </summary>
	Pending = 4,

	/// <summary>
	///     The setter raised after it started, or the state could not be read back after the setter: the effect is unknown.
	/// </summary>
	Indeterminate = 5,

	/// <summary>
	///     The command was refused before the setter: see <see cref="MemoryRecordActivationOutcome.Problem" /> (record not
	///     found, table load in progress, runtime identity changed, lookup failure).
	/// </summary>
	NotAttempted = 6
}
