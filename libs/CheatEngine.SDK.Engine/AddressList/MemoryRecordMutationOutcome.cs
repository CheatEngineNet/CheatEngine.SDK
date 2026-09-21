using CheatEngine.SDK.Lua.Calls;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>A structured result from a typed address-list record mutation.</summary>
/// <remarks>
///     <para>
///         The result describes the command only. It intentionally does not capture a record snapshot: callers that
///         need a post-command view must obtain a fresh snapshot after a <see cref="MemoryRecordMutationEffect.Completed" />
///         result, and must not merge a snapshot-read failure with the mutation result.
///     </para>
///     <para>
///         <see cref="MemoryRecordMutationEffect.Indeterminate" /> means a protected call began and then failed. Do
///         not blindly retry it; CE may have applied part or all of the mutation before reporting the error.
///     </para>
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
    public MemoryRecordMutationEffect Effect { get; }

    /// <summary>Gets the stable problem classification, or <see cref="MemoryRecordMutationProblem.None" /> on success.</summary>
    public MemoryRecordMutationProblem Problem { get; }

    /// <summary>Gets the protected Lua status for <see cref="MemoryRecordMutationProblem.LuaFailure" /> only.</summary>
    public LuaStatus LuaStatus { get; }

    /// <summary>Gets whether CE reported that the mutation completed.</summary>
    public bool IsCompleted => Effect == MemoryRecordMutationEffect.Completed;
}
