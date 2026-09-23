using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>The factual result of one <see cref="AddressListMutations.SetActive" /> command.</summary>
/// <remarks>
///     <see cref="Effect" /> follows from <see cref="Kind" />: <see cref="MemoryRecordActivationOutcomeKind.Applied" /> is
///     <see cref="EngineEffectState.Applied" />; <see cref="MemoryRecordActivationOutcomeKind.Unchanged" /> and
///     <see cref="MemoryRecordActivationOutcomeKind.NotAttempted" /> are <see cref="EngineEffectState.NotStarted" />; every
///     other kind is <see cref="EngineEffectState.Unknown" />. The before and after states are the values actually read;
///     <see langword="null" /> means that no value was observed.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct MemoryRecordActivationOutcome
{
	internal MemoryRecordActivationOutcome(MemoryRecordActivationOutcomeKind kind, MemoryRecordMutationProblem problem,
		bool requestedActive, bool? activeBefore, bool? activeAfter, bool? asyncProcessingAfter, LuaStatus luaStatus)
	{
		Kind = kind;
		Problem = problem;
		RequestedActive = requestedActive;
		ActiveBefore = activeBefore;
		ActiveAfter = activeAfter;
		AsyncProcessingAfter = asyncProcessingAfter;
		LuaStatus = luaStatus;
	}

	/// <summary>Gets the category of the command.</summary>
	public MemoryRecordActivationOutcomeKind Kind
	{
		get;
	}

	/// <summary>Gets how far the activation went, derived from <see cref="Kind" /> only.</summary>
	public EngineEffectState Effect => Kind switch
	{
		MemoryRecordActivationOutcomeKind.Applied => EngineEffectState.Applied,
		MemoryRecordActivationOutcomeKind.Unchanged or MemoryRecordActivationOutcomeKind.NotAttempted =>
			EngineEffectState.NotStarted,
		_ => EngineEffectState.Unknown
	};

	/// <summary>
	///     Gets the reason for <see cref="MemoryRecordActivationOutcomeKind.NotAttempted" />,
	///     <see cref="MemoryRecordMutationProblem.LuaFailure" /> or <see cref="MemoryRecordMutationProblem.InvalidResult" />
	///     for <see cref="MemoryRecordActivationOutcomeKind.Indeterminate" />, and
	///     <see cref="MemoryRecordMutationProblem.None" /> otherwise.
	/// </summary>
	public MemoryRecordMutationProblem Problem
	{
		get;
	}

	/// <summary>Gets the requested <c>Active</c> state.</summary>
	public bool RequestedActive
	{
		get;
	}

	/// <summary>Gets the <c>Active</c> state read before the setter, or <see langword="null" /> when it was not read.</summary>
	public bool? ActiveBefore
	{
		get;
	}

	/// <summary>Gets the <c>Active</c> state read after the setter, or <see langword="null" /> when it was not observed.</summary>
	public bool? ActiveAfter
	{
		get;
	}

	/// <summary>
	///     Gets the <c>AsyncProcessing</c> state read after the setter, or <see langword="null" /> when it was not observed.
	/// </summary>
	public bool? AsyncProcessingAfter
	{
		get;
	}

	/// <summary>Gets the protected Lua status of a failed access; otherwise <see cref="LuaStatus.Ok" />.</summary>
	public LuaStatus LuaStatus
	{
		get;
	}
}
