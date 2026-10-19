using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>A structured result from a typed address-list record mutation.</summary>
/// <remarks>
/// <para>
///     The result describes the command only. It intentionally does not capture a record snapshot: callers that need a
///     post-command view must obtain a fresh snapshot after a <see cref="MemoryRecordMutationEffect.Completed" />
///     result, and must not merge a snapshot-read failure with the mutation result. Parent assignment is an exception to
///     the usual command-only shape: it reports <see cref="MemoryRecordMutationEffect.Completed" /> only after a fresh
///     child lookup confirms the requested parent identifier.
/// </para>
/// <para>
///     <see cref="MemoryRecordMutationEffect.Indeterminate" /> means a protected call started but either failed or left
///     its postcondition unconfirmed. Do not blindly retry it; CE may have applied part or all of the mutation before
///     reporting the error or invalid result.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MemoryRecordMutationOutcome
{
	internal MemoryRecordMutationOutcome(MemoryRecordMutationEffect effect, MemoryRecordMutationProblem problem,
		LuaStatus luaStatus)
	{
		Effect = effect;
		Problem = problem;
		LuaStatus = luaStatus;
	}

	/// <summary>Gets how far the mutation progressed.</summary>
	public MemoryRecordMutationEffect Effect
	{
		get;
	}

	/// <summary>Gets the stable problem classification, or <see cref="MemoryRecordMutationProblem.None" /> on success.</summary>
	public MemoryRecordMutationProblem Problem
	{
		get;
	}

	/// <summary>Gets the protected Lua status for <see cref="MemoryRecordMutationProblem.LuaFailure" /> only.</summary>
	public LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>Gets whether CE reported that the mutation completed.</summary>
	public bool IsCompleted => Effect == MemoryRecordMutationEffect.Completed;
}
