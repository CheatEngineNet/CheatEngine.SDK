namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>Classifies a record-mutation outcome without exposing transient Lua error text.</summary>
public enum MemoryRecordMutationProblem
{
	/// <summary>No mutation outcome has been produced.</summary>
	Uninitialized,

	/// <summary>The operation completed.</summary>
	None,

	/// <summary>The current CE address list was not available.</summary>
	AddressListUnavailable,

	/// <summary>The requested child record was not found in the current address list.</summary>
	RecordNotFound,

	/// <summary>The requested parent record was not found in the current address list.</summary>
	ParentNotFound,

	/// <summary>A record was requested as its own parent.</summary>
	SelfParent,

	/// <summary>The requested hierarchy would contain a cycle.</summary>
	CycleDetected,

	/// <summary>The parent walk reached its explicit safety limit.</summary>
	TraversalLimitReached,

	/// <summary>A required CE global or member was absent or not callable.</summary>
	GlobalUnavailable,

	/// <summary>A protected CE access or call raised.</summary>
	LuaFailure,

	/// <summary>CE returned a value outside the typed contract.</summary>
	InvalidResult,

	/// <summary>
	///     A table load (<see cref="Tables.CheatTableFiles.TryLoad" />) is running on the calling thread, so the mutation
	///     was refused before any Lua call: the record identifiers it would resolve are being replaced.
	/// </summary>
	TableLoadInProgress = 11,

	/// <summary>
	///     The Lua runtime identity (attach epoch or state generation) changed between the preflight and the mutation, so
	///     the mutation was not attempted.
	/// </summary>
	RuntimeIdentityChanged = 12
}
