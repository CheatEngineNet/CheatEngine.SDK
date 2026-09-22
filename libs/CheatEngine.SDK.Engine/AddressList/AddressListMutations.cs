using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>Typed, ID-addressed mutations for records owned by Cheat Engine's current GUI address list.</summary>
/// <remarks>
///     <para>
///         This is deliberately a command surface, not a live-record lease. A <see cref="MemoryRecordId" /> is resolved
///         against the current list inside each one-operation command because CE can replace the list or rebuild its
///         records. It therefore does not promise that an identifier proves the identity of a historical record across
///         a table reload or re-enable.
///     </para>
///     <para>
///         All new arguments and results are typed values; no <see cref="CEObject" /> crosses this public boundary.
///         Existing borrowed <see cref="MemoryRecord" /> and <see cref="AddressList" /> wrappers remain compatibility
///         views and are not ownership capabilities.
///     </para>
/// </remarks>
public static class AddressListMutations
{
	private static readonly LuaRef SGetAddressList = new();

	/// <summary>Deletes the current address-list record with <paramref name="recordId" />.</summary>
	/// <param name="recordId">The CE <c>MemoryRecord.ID</c> to resolve in the current list.</param>
	/// <returns>A command result that distinguishes preflight rejection from an invoked but indeterminate destroy.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static MemoryRecordMutationOutcome Delete(MemoryRecordId recordId)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		LuaStateIdentity identity = LuaRuntime.CurrentStateIdentity;
		int top = state.Top;
		bool mutationStarted = false;
		try
		{
			MemoryRecordMutationOutcome preflight = TryGetCurrentList(state, out AddressList list);
			if (preflight.Problem != MemoryRecordMutationProblem.None)
			{
				return preflight;
			}

			preflight = TryResolveRecord(state, list, recordId, false, out MemoryRecord record);
			if (preflight.Problem != MemoryRecordMutationProblem.None)
			{
				return preflight;
			}

			if (LuaRuntime.CurrentStateIdentity != identity)
			{
				return NotAttempted(MemoryRecordMutationProblem.GlobalUnavailable);
			}

			LuaStatus status = record.Handle.TryPushMethodLeavingObject(state, "destroy"u8);
			if (!status.IsOk)
			{
				return FromPreflightStatus(status);
			}

			mutationStarted = true;
			status = state.TryCall(0, 0);
			return status.IsOk ? Completed() : Indeterminate(status);
		}
		catch (LuaException exception)
		{
			return mutationStarted
				? Indeterminate(exception.Status)
				: NotAttempted(MemoryRecordMutationProblem.LuaFailure,
					exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Assigns a record's parent after validating the requested hierarchy with the default traversal bound.</summary>
	/// <param name="recordId">The child record's CE ID in the current list.</param>
	/// <param name="parentId">The new parent's CE ID, or <see langword="null" /> to make the child a root record.</param>
	/// <returns>A command result that preserves whether assignment was started.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static MemoryRecordMutationOutcome SetParent(MemoryRecordId recordId, MemoryRecordId? parentId)
	{
		return SetParent(recordId, parentId, MemoryRecordParentTraversalLimit.Default);
	}

	/// <summary>Assigns a record's parent after validating the requested hierarchy with <paramref name="traversalLimit" />.</summary>
	/// <param name="recordId">The child record's CE ID in the current list.</param>
	/// <param name="parentId">The new parent's CE ID, or <see langword="null" /> to make the child a root record.</param>
	/// <param name="traversalLimit">A positive bound for the proposed parent's existing parent chain.</param>
	/// <returns>A command result that preserves whether assignment was started.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "The parent-assignment transaction is intentionally kept atomic around the Lua operation.")]
	public static MemoryRecordMutationOutcome SetParent(MemoryRecordId recordId, MemoryRecordId? parentId,
		MemoryRecordParentTraversalLimit traversalLimit)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(traversalLimit.MaximumHops, nameof(traversalLimit));
		if (parentId.HasValue && parentId.Value == recordId)
		{
			return NotAttempted(MemoryRecordMutationProblem.SelfParent);
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		LuaStateIdentity identity = LuaRuntime.CurrentStateIdentity;
		int top = state.Top;
		bool mutationStarted = false;
		try
		{
			MemoryRecordMutationOutcome preflight = TryPrepareParentAssignment(state, recordId, parentId,
				traversalLimit, out MemoryRecord child, out MemoryRecord parent);
			if (preflight.Problem != MemoryRecordMutationProblem.None)
			{
				return preflight;
			}

			if (LuaRuntime.CurrentStateIdentity != identity)
			{
				return NotAttempted(MemoryRecordMutationProblem.GlobalUnavailable);
			}

			if (parentId.HasValue)
			{
				parent.Handle.Push(state);
			}
			else
			{
				state.PushNil();
			}

			mutationStarted = true;
			LuaStatus status = child.Handle.TrySetProperty(state, "Parent"u8);
			return status.IsOk ? Completed() : Indeterminate(status);
		}
		catch (LuaException exception)
		{
			return mutationStarted
				? Indeterminate(exception.Status)
				: NotAttempted(MemoryRecordMutationProblem.LuaFailure,
					exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static MemoryRecordMutationOutcome TryPrepareParentAssignment(LuaState state, MemoryRecordId recordId,
		MemoryRecordId? parentId, MemoryRecordParentTraversalLimit traversalLimit, out MemoryRecord child,
		out MemoryRecord parent)
	{
		child = default;
		parent = default;
		MemoryRecordMutationOutcome preflight = TryGetCurrentList(state, out AddressList list);
		if (preflight.Problem != MemoryRecordMutationProblem.None)
		{
			return preflight;
		}

		preflight = TryResolveRecord(state, list, recordId, false, out child);
		if (preflight.Problem != MemoryRecordMutationProblem.None)
		{
			return preflight;
		}

		if (!parentId.HasValue)
		{
			return Completed();
		}

		preflight = TryResolveRecord(state, list, parentId.Value, true, out parent);
		if (preflight.Problem != MemoryRecordMutationProblem.None)
		{
			return preflight;
		}

		return ValidateParentChain(state, recordId, parent, traversalLimit);
	}

	private static MemoryRecordMutationOutcome TryResolveRecord(LuaState state, AddressList list, MemoryRecordId id,
		bool isParent, out MemoryRecord record)
	{
		LuaStatus status = list.Handle.TryPushMethodLeavingObject(state, "getMemoryRecordByID"u8);
		if (!status.IsOk)
		{
			record = default;
			return FromPreflightStatus(status);
		}

		MemoryRecordId.Push(state, id);
		status = state.TryCall(1, 1);
		if (!status.IsOk)
		{
			record = default;
			return NotAttempted(MemoryRecordMutationProblem.LuaFailure, status);
		}

		if (state.IsNil(-1))
		{
			record = default;
			return NotAttempted(isParent
				? MemoryRecordMutationProblem.ParentNotFound
				: MemoryRecordMutationProblem.RecordNotFound);
		}

		if (!MemoryRecord.TryRead(state, -1, out record))
		{
			return NotAttempted(MemoryRecordMutationProblem.InvalidResult);
		}

		return Completed();
	}

	private static MemoryRecordMutationOutcome TryGetCurrentList(LuaState state, out AddressList list)
	{
		LuaGlobalPushOutcome global = LuaGlobalFunctions.TryPushWithOutcome(state, SGetAddressList, "getAddressList"u8);
		if (!global.IsSuccess)
		{
			list = default;
			return global.Status == LuaGlobalPushStatus.LuaFailure
				? NotAttempted(MemoryRecordMutationProblem.LuaFailure, global.LuaStatus)
				: NotAttempted(MemoryRecordMutationProblem.GlobalUnavailable);
		}

		LuaStatus status = state.TryCall(0, 1);
		if (!status.IsOk)
		{
			list = default;
			return NotAttempted(MemoryRecordMutationProblem.LuaFailure, status);
		}

		if (state.IsNil(-1))
		{
			list = default;
			return NotAttempted(MemoryRecordMutationProblem.AddressListUnavailable);
		}

		if (!AddressList.TryRead(state, -1, out list))
		{
			return NotAttempted(MemoryRecordMutationProblem.InvalidResult);
		}

		return Completed();
	}

	private static MemoryRecordMutationOutcome ValidateParentChain(LuaState state, MemoryRecordId childId,
		MemoryRecord proposedParent, MemoryRecordParentTraversalLimit traversalLimit)
	{
		HashSet<MemoryRecordId> seen = new();
		MemoryRecord current = proposedParent;
		for (int hops = 0;; hops++)
		{
			LuaStatus status = current.Handle.TryGetProperty(state, "ID"u8);
			if (!status.IsOk)
			{
				return FromPreflightStatus(status);
			}

			if (!MemoryRecordId.TryRead(state, -1, out MemoryRecordId currentId))
			{
				return NotAttempted(MemoryRecordMutationProblem.InvalidResult);
			}

			if (currentId == childId || !seen.Add(currentId))
			{
				return NotAttempted(MemoryRecordMutationProblem.CycleDetected);
			}

			status = current.Handle.TryGetProperty(state, "Parent"u8);
			if (!status.IsOk)
			{
				return FromPreflightStatus(status);
			}

			if (state.IsNil(-1))
			{
				return Completed();
			}

			if (!MemoryRecord.TryRead(state, -1, out current))
			{
				return NotAttempted(MemoryRecordMutationProblem.InvalidResult);
			}

			if (hops + 1 >= traversalLimit.MaximumHops)
			{
				return NotAttempted(MemoryRecordMutationProblem.TraversalLimitReached);
			}
		}
	}

	private static MemoryRecordMutationOutcome FromPreflightStatus(LuaStatus status)
	{
		return NotAttempted(MemoryRecordMutationProblem.LuaFailure, status);
	}

	private static MemoryRecordMutationOutcome Completed()
	{
		return new MemoryRecordMutationOutcome(MemoryRecordMutationEffect.Completed, MemoryRecordMutationProblem.None,
			LuaStatus.Ok);
	}

	private static MemoryRecordMutationOutcome NotAttempted(MemoryRecordMutationProblem problem)
	{
		return new MemoryRecordMutationOutcome(MemoryRecordMutationEffect.NotAttempted, problem, LuaStatus.Ok);
	}

	private static MemoryRecordMutationOutcome NotAttempted(MemoryRecordMutationProblem problem, LuaStatus status)
	{
		return new MemoryRecordMutationOutcome(MemoryRecordMutationEffect.NotAttempted, problem, status);
	}

	private static MemoryRecordMutationOutcome Indeterminate(LuaStatus status)
	{
		return new MemoryRecordMutationOutcome(MemoryRecordMutationEffect.Indeterminate,
			MemoryRecordMutationProblem.LuaFailure, status);
	}
}
