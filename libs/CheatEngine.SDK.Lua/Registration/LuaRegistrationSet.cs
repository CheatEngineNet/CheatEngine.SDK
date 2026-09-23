using System;
using System.Collections.Generic;
using System.Text;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Publishes static Lua functions as one ownership-aware registration transaction.</summary>
public static class LuaRegistrationSet
{
	/// <summary>Publishes every entry and returns the lease that owns exactly the installed effective globals.</summary>
	/// <param name="state">The current calling thread's state of an attached runtime.</param>
	/// <param name="entries">The nonempty, uniquely named static registrations to publish.</param>
	/// <param name="collisionPolicy">The explicit handling for existing effective globals.</param>
	/// <returns>
	///     The factual registration outcome and a lease on success or after a failed compensation that left a residual
	///     owner.
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="state" /> is null, entries are empty, duplicated, or invalid.</exception>
	public static LuaRegistrationResult Register(LuaState state, ReadOnlySpan<LuaRegistrationEntry> entries,
		LuaRegistrationCollisionPolicy collisionPolicy = LuaRegistrationCollisionPolicy.RejectExisting)
	{
		if (state.IsNull)
		{
			throw new ArgumentException("A registration set needs a non-null Lua state.", nameof(state));
		}

		if (entries.IsEmpty)
		{
			throw new ArgumentException("A registration set needs at least one entry.", nameof(entries));
		}

		if (collisionPolicy is not LuaRegistrationCollisionPolicy.RejectExisting
			and not LuaRegistrationCollisionPolicy.ReplaceExisting)
		{
			throw new ArgumentOutOfRangeException(nameof(collisionPolicy));
		}

		ValidateEntries(entries);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation(state);
		LuaStateIdentity identity = LuaRuntime.CurrentStateIdentity;
		LeaseEntry[] entriesToLease = new LeaseEntry[entries.Length];
		LuaRegistrationResult? preflight = Preflight(state, entries, collisionPolicy, entriesToLease);
		if (preflight is not null)
		{
			return preflight.Value;
		}

		return Publish(state, identity, entries, entriesToLease);
	}

	internal static LuaRegistrationReleaseOutcome Release(LuaState state, LuaStateIdentity identity,
		LeaseEntry[] entries, bool retainFailures, out LeaseEntry[]? residual)
	{
		residual = null;
		if (!LuaRuntime.IsAttached || identity != LuaRuntime.CurrentStateIdentity)
		{
			Forget(entries);
			return LuaRegistrationReleaseOutcome.Stale(entries.Length);
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation(state);
		if (!LuaRuntime.IsAttached || identity != LuaRuntime.CurrentStateIdentity)
		{
			Forget(entries);
			return LuaRegistrationReleaseOutcome.Stale(entries.Length);
		}

		ReleaseAccumulator accumulator = new(entries.Length, retainFailures);
		int top = state.Top;

		for (int index = 0; index < entries.Length; index++)
		{
			ReleaseEntry(state, entries[index], top, accumulator);
		}

		return accumulator.CreateOutcome(out residual);
	}

	private static LuaRegistrationResult? Preflight(LuaState state, ReadOnlySpan<LuaRegistrationEntry> entries,
		LuaRegistrationCollisionPolicy collisionPolicy, LeaseEntry[] entriesToLease)
	{
		int top = state.Top;
		for (int index = 0; index < entries.Length; index++)
		{
			LuaRegistrationEntry entry = entries[index];
			LuaStatus status = state.TryGetGlobal(entry.Utf8Name);
			if (!status.IsOk)
			{
				return PreflightFailed(state, top, entriesToLease, index, entry.Name, status);
			}

			bool hadPreviousValue = !state.IsNil(-1);
			if (hadPreviousValue && collisionPolicy == LuaRegistrationCollisionPolicy.RejectExisting)
			{
				state.SetTop(top);
				ReleaseEntries(state, entriesToLease, index);
				return Collision(entry.Name);
			}

			LuaRef? previous = null;
			if (hadPreviousValue)
			{
				state.PushValue(-1);
				status = state.TryCreateRef(out previous);
				if (!status.IsOk || previous is null)
				{
					return PreflightFailed(state, top, entriesToLease, index, entry.Name, status);
				}
			}

			entriesToLease[index] = new LeaseEntry(entry.Name, previous);
			state.SetTop(top);
		}

		return null;
	}

	private static LuaRegistrationResult Publish(LuaState state, LuaStateIdentity identity,
		ReadOnlySpan<LuaRegistrationEntry> entries, LeaseEntry[] entriesToLease)
	{
		int top = state.Top;
		for (int index = 0; index < entries.Length; index++)
		{
			LuaRegistrationEntry entry = entries[index];
			LuaStatus status = LuaRuntime.TryPushGeneratedFunction(state, entry.Function);
			if (!status.IsOk)
			{
				return PublishFailure(state, top, identity, entriesToLease, index, entry.Name, status);
			}

			state.PushValue(-1);
			status = state.TryCreateRef(out LuaRef? installed);
			if (!status.IsOk || installed is null)
			{
				return PublishFailure(state, top, identity, entriesToLease, index, entry.Name, status);
			}

			entriesToLease[index].Installed = installed;
			status = state.TrySetGlobal(entry.Utf8Name);
			if (!status.IsOk)
			{
				return PublishFailure(state, top, identity, entriesToLease, index + 1, entry.Name, status);
			}

			state.SetTop(top);
		}

		return new LuaRegistrationResult(LuaRegistrationResultKind.Succeeded, null,
			LuaRegistrationReleaseOutcome.NotAttempted(), new LuaRegistrationLease(identity, entriesToLease));
	}

	private static LuaRegistrationResult PreflightFailed(LuaState state, int top, LeaseEntry[] entries, int count,
		string name, LuaStatus status)
	{
		state.SetTop(top);
		ReleaseEntries(state, entries, count);
		return Failed(LuaRegistrationResultKind.PreflightFailed, name, status);
	}

	private static LuaRegistrationResult PublishFailure(LuaState state, int top, LuaStateIdentity identity,
		LeaseEntry[] entries, int count, string name, LuaStatus status)
	{
		state.SetTop(top);
		return PublishFailed(state, identity, entries, count, name, status);
	}

	private static void ReleaseEntry(LuaState state, LeaseEntry entry, int top, ReleaseAccumulator accumulator)
	{
		if (entry.Installed is null)
		{
			ReleaseReferences(state, entry);
			return;
		}

		LuaStatus status = state.TryGetGlobal(entry.Utf8Name);
		if (!status.IsOk)
		{
			state.SetTop(top);
			accumulator.Failed(state, entry, status);
			return;
		}

		if (!state.TryPushRef(entry.Installed))
		{
			state.SetTop(top);
			accumulator.Failed(state, entry, LuaStatus.RuntimeError);
			return;
		}

		bool ownsCurrentValue = state.RawEquals(-2, -1);
		state.SetTop(top);
		if (!ownsCurrentValue)
		{
			accumulator.Replaced(state, entry);
			return;
		}

		if (!TryPushPrevious(state, entry))
		{
			state.SetTop(top);
			accumulator.Failed(state, entry, LuaStatus.RuntimeError);
			return;
		}

		status = state.TrySetGlobal(entry.Utf8Name);
		state.SetTop(top);
		if (!status.IsOk)
		{
			accumulator.Failed(state, entry, status);
			return;
		}

		accumulator.Released(state, entry);
	}

	private static bool TryPushPrevious(LuaState state, LeaseEntry entry)
	{
		if (entry.Previous is null)
		{
			state.PushNil();
			return true;
		}

		return state.TryPushRef(entry.Previous);
	}

	internal static void Forget(LeaseEntry[] entries)
	{
		for (int index = 0; index < entries.Length; index++)
		{
			Forget(entries[index]);
		}
	}

	private static LuaRegistrationResult PublishFailed(LuaState state, LuaStateIdentity identity,
		LeaseEntry[] entries, int count, string name, LuaStatus status)
	{
		LeaseEntry[] published = new LeaseEntry[count];
		Array.Copy(entries, published, count);
		LuaRegistrationReleaseOutcome rollback = Release(state, identity, published, true, out LeaseEntry[]? residual);
		ReleaseEntries(state, entries, count, entries.Length);
		LuaRegistrationLease? lease = residual is null ? null : new LuaRegistrationLease(identity, residual);
		return new LuaRegistrationResult(LuaRegistrationResultKind.PublicationFailed,
			new LuaRegistrationFailure(name, status), rollback, lease);
	}

	private static LuaRegistrationResult Collision(string name)
	{
		return new LuaRegistrationResult(LuaRegistrationResultKind.Collision,
			new LuaRegistrationFailure(name, LuaStatus.Ok), LuaRegistrationReleaseOutcome.NotAttempted(), null);
	}

	private static LuaRegistrationResult Failed(LuaRegistrationResultKind kind, string name, LuaStatus status)
	{
		return new LuaRegistrationResult(kind, new LuaRegistrationFailure(name, status),
			LuaRegistrationReleaseOutcome.NotAttempted(), null);
	}

	private static void ReleaseReferences(LuaState state, LeaseEntry entry)
	{
		try
		{
			entry.Installed?.Release(state);
		}
		finally
		{
			entry.Previous?.Release(state);
		}
	}

	private static void Forget(LeaseEntry entry)
	{
		entry.Installed?.Release(default);
		entry.Previous?.Release(default);
	}

	private static void ReleaseEntries(LuaState state, LeaseEntry[] entries, int count)
	{
		ReleaseEntries(state, entries, 0, count);
	}

	private static void ReleaseEntries(LuaState state, LeaseEntry[] entries, int start, int end)
	{
		for (int index = start; index < end; index++)
		{
			ReleaseReferences(state, entries[index]);
		}
	}

	private static void ValidateEntries(ReadOnlySpan<LuaRegistrationEntry> entries)
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		for (int index = 0; index < entries.Length; index++)
		{
			LuaRegistrationEntry entry = entries[index];
			if (string.IsNullOrEmpty(entry.Name) || entry.Function.IsNull)
			{
				throw new ArgumentException("Every registration entry needs a nonempty name and non-null thunk.",
					nameof(entries));
			}

			if (!names.Add(entry.Name))
			{
				throw new ArgumentException("A registration set cannot contain duplicate global names.",
					nameof(entries));
			}
		}
	}

	internal sealed class LeaseEntry
	{
		internal LeaseEntry(string name, LuaRef? previous)
		{
			Name = name;
			Utf8Name = Encoding.UTF8.GetBytes(name);
			Previous = previous;
		}

		internal string Name
		{
			get;
		}

		internal byte[] Utf8Name
		{
			get;
		}

		internal LuaRef? Installed
		{
			get;
			set;
		}

		internal LuaRef? Previous
		{
			get;
		}
	}

	private sealed class ReleaseAccumulator
	{
		private readonly LuaRegistrationReleaseFailure[] _failures;
		private readonly LeaseEntry[]? _residual;
		private int _failureCount;
		private int _removedCount;
		private int _replacementCount;
		private int _residualCount;
		private int _restoredCount;

		internal ReleaseAccumulator(int count, bool retainFailures)
		{
			_failures = new LuaRegistrationReleaseFailure[count];
			_residual = retainFailures ? new LeaseEntry[count] : null;
		}

		internal void Failed(LuaState state, LeaseEntry entry, LuaStatus status)
		{
			_failures[_failureCount++] = new LuaRegistrationReleaseFailure(entry.Name, status);
			if (_residual is not null)
			{
				_residual[_residualCount++] = entry;
				return;
			}

			ReleaseReferences(state, entry);
		}

		internal void Replaced(LuaState state, LeaseEntry entry)
		{
			_replacementCount++;
			ReleaseReferences(state, entry);
		}

		internal void Released(LuaState state, LeaseEntry entry)
		{
			if (entry.Previous is null)
			{
				_removedCount++;
			}
			else
			{
				_restoredCount++;
			}

			ReleaseReferences(state, entry);
		}

		internal LuaRegistrationReleaseOutcome CreateOutcome(out LeaseEntry[]? residual)
		{
			residual = CopyResidual();
			LuaRegistrationReleaseFailure[] failures = CopyFailures();
			LuaRegistrationReleaseKind kind = _failureCount == 0
				? LuaRegistrationReleaseKind.Released
				: LuaRegistrationReleaseKind.PartiallyReleased;
			int remainingCount = _residual is null ? _failureCount : _residualCount;
			return new LuaRegistrationReleaseOutcome(kind, _removedCount, _restoredCount, _replacementCount,
				remainingCount, failures);
		}

		private LuaRegistrationReleaseFailure[] CopyFailures()
		{
			if (_failureCount == 0)
			{
				return Array.Empty<LuaRegistrationReleaseFailure>();
			}

			LuaRegistrationReleaseFailure[] copy = new LuaRegistrationReleaseFailure[_failureCount];
			Array.Copy(_failures, copy, _failureCount);
			return copy;
		}

		private LeaseEntry[]? CopyResidual()
		{
			if (_residualCount == 0)
			{
				return null;
			}

			LeaseEntry[] copy = new LeaseEntry[_residualCount];
			Array.Copy(_residual!, copy, _residualCount);
			return copy;
		}
	}
}
