using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;

using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>
///     The bounded, exhaustive AOB route of <see cref="AobScanner" />: one MemScan/FoundList session, a hexadecimal
///     byte-array first scan over <c>[Start, Stop)</c> with <c>OnlyOneResult</c> off, a wait, and an address-only,
///     start-post-filtered copy into a caller-bounded destination. Every exit releases the session once, child before
///     parent.
/// </summary>
internal static class AobBoundedScan
{
	private const string ContextOperation = "AobScanner.TryScanWithinBounds";

	internal static AobBoundedScanResult Run(string pattern, AobScanBounds bounds, AobScanOptions options,
		int? waitMilliseconds, Span<Address> destination, CancellationToken cancellationToken)
	{
		return Run(pattern, bounds, options, waitMilliseconds, destination, ArrayPool<Address>.Shared,
			cancellationToken);
	}

	// stagingPool is a test seam, not an extension point: production always passes ArrayPool<Address>.Shared. Tests
	// substitute a pool whose Rent fails to prove that a managed allocation failure after the session exists still
	// releases it once and propagates unchanged (audit ch.08, F13).
	internal static AobBoundedScanResult Run(string pattern, AobScanBounds bounds, AobScanOptions options,
		int? waitMilliseconds, Span<Address> destination, ArrayPool<Address> stagingPool,
		CancellationToken cancellationToken)
	{
		if (!bounds.IsValid)
		{
			return Refused(AobBoundedScanOutcomeKind.InvalidBounds);
		}

		RequireEnabledMainThread();
		if (cancellationToken.IsCancellationRequested)
		{
			return Refused(AobBoundedScanOutcomeKind.Cancelled);
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		long started = Stopwatch.GetTimestamp();
		AobBoundedScanFacts facts = new()
		{
			Creation = MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? session),
			Termination = MemoryScanTerminationStatus.NotRequired
		};
		if (facts.Creation.Status != MemoryScanCreationStatus.Success || session is null)
		{
			facts.Kind = AobBoundedScanOutcomeKind.SessionCreationFailed;
		}
		else
		{
			// Nothing between the session's publication and RunSession's releasing try may allocate or throw.
			RunSession(state, session, pattern, bounds, options, waitMilliseconds, destination, stagingPool,
				ref facts, cancellationToken);
		}

		facts.TotalElapsed = Stopwatch.GetElapsedTime(started);
		return new AobBoundedScanResult(in facts);
	}

	// Releases the session once whatever happens. The staging buffer is rented inside the releasing try, so a managed
	// allocation failure (a destination too large for one array, memory pressure) still releases the found list and
	// then its scanner, and the original exception propagates (audit ch.08, F13). In-bounds addresses are staged so that
	// nothing reaches the caller's destination unless the scan succeeds.
	private static void RunSession(LuaState state, MemoryScanSession session, string pattern, AobScanBounds bounds,
		AobScanOptions options, int? waitMilliseconds, Span<Address> destination, ArrayPool<Address> stagingPool,
		ref AobBoundedScanFacts facts, CancellationToken cancellationToken)
	{
		Address[]? staging = null;
		try
		{
			try
			{
				staging = stagingPool.Rent(destination.Length);
				facts.Kind = Execute(state, session, pattern, bounds, options, waitMilliseconds,
					staging.AsSpan(0, destination.Length), ref facts, cancellationToken);
			}
			catch (MemoryScanException exception)
			{
				facts.Kind = FromException(exception, ref facts);
			}
			finally
			{
				facts.Release = session.ReleaseWithOutcome();
			}

			if (staging is not null &&
			    facts.Kind is AobBoundedScanOutcomeKind.Matches or AobBoundedScanOutcomeKind.NoMatches)
			{
				staging.AsSpan(0, facts.Written).CopyTo(destination);
			}
			else
			{
				facts.Written = 0;
			}
		}
		finally
		{
			if (staging is not null)
			{
				stagingPool.Return(staging);
			}
		}
	}

	private static AobBoundedScanOutcomeKind Execute(LuaState state, MemoryScanSession session, string pattern,
		AobScanBounds bounds, AobScanOptions options, int? waitMilliseconds, Span<Address> staged,
		ref AobBoundedScanFacts facts, CancellationToken cancellationToken)
	{
		// Exhaustive by construction (F07, A13-07): OnlyOneResult is always switched off before the first scan, and
		// IsUnique, Result, getOnlyResult and the scan callbacks are never touched.
		LuaStatus onlyOne = session.SetOnlyOneResultCore(state, false);
		if (!onlyOne.IsOk)
		{
			facts.LuaStatus = FailureStatus(onlyOne);
			return AobBoundedScanOutcomeKind.ScanFailed;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return AobBoundedScanOutcomeKind.Cancelled;
		}

		// An omitted protection string is CE's "find everything" value, never a nil (celua.txt line 847).
		FirstScanRequest request = FirstScanRequest.ByteArray(pattern, bounds.Start, bounds.Stop,
			options.ProtectionFlags ?? string.Empty, options.AlignmentMethod,
			options.AlignmentParameter ?? string.Empty);
		long scanStarted = Stopwatch.GetTimestamp();
		session.StartFirstScan(in request);
		AobBoundedScanOutcomeKind waited = Wait(state, session, waitMilliseconds, ref facts);
		if (waited != AobBoundedScanOutcomeKind.Unknown)
		{
			return waited;
		}

		facts.HostScanElapsed = Stopwatch.GetElapsedTime(scanStarted);
		if (cancellationToken.IsCancellationRequested)
		{
			return AobBoundedScanOutcomeKind.Cancelled;
		}

		MemoryScanMaterializationStatus context = session.TryEnsureCurrentContextCore(state);
		if (context != MemoryScanMaterializationStatus.Success)
		{
			return FromContext(context);
		}

		long copyStarted = Stopwatch.GetTimestamp();
		try
		{
			return Copy(state, session, bounds, staged, ref facts, cancellationToken);
		}
		finally
		{
			facts.CopyElapsed = Stopwatch.GetElapsedTime(copyStarted);
		}
	}

	// Returns Unknown when the scan completed and its results are readable; otherwise the terminal outcome.
	private static AobBoundedScanOutcomeKind Wait(LuaState state, MemoryScanSession session, int? waitMilliseconds,
		ref AobBoundedScanFacts facts)
	{
		MemoryScanMaterializationStatus context = session.TryEnsureCurrentContextCore(state);
		if (context != MemoryScanMaterializationStatus.Success)
		{
			return FromContext(context);
		}

		MemoryScanWaitStatus waited = session.WaitCore(state, waitMilliseconds, true, out LuaStatus luaStatus);
		switch (waited)
		{
			case MemoryScanWaitStatus.Completed:
				return AobBoundedScanOutcomeKind.Unknown;
			case MemoryScanWaitStatus.TimedOut:
				// The call deadline expired: one cooperative stop and one bounded settle wait, never forced or retried.
				facts.Termination =
					session.TerminateAndSettleCore(state, MemoryScanSession.ReleaseTerminationWaitMilliseconds);
				return AobBoundedScanOutcomeKind.WaitTimedOut;
			case MemoryScanWaitStatus.InvalidResult:
				return AobBoundedScanOutcomeKind.InvalidResult;
			default:
				facts.LuaStatus = FailureStatus(luaStatus);
				return AobBoundedScanOutcomeKind.ScanFailed;
		}
	}

	private static AobBoundedScanOutcomeKind Copy(LuaState state, MemoryScanSession session, AobScanBounds bounds,
		Span<Address> staged, ref AobBoundedScanFacts facts, CancellationToken cancellationToken)
	{
		ulong count = session.ReadResultCountCore(state);
		facts.HostResultCount = count;
		facts.UnreadHostRows = count;
		ReadHostErrorText(state, session, ref facts);
		AobBoundedScanOutcomeKind rows = ReadRows(state, session, bounds, staged, count, ref facts,
			cancellationToken, out int staging);
		if (rows != AobBoundedScanOutcomeKind.Unknown)
		{
			return rows;
		}

		facts.Written = staging;
		facts.IsMaterializationLimitReached = staging == staged.Length && facts.UnreadHostRows > 0;
		if (staging > 0)
		{
			return AobBoundedScanOutcomeKind.Matches;
		}

		// A present error text is reported by presence only; its content is never parsed (A07-23, A24-23).
		return facts.HostErrorText is null
			? AobBoundedScanOutcomeKind.NoMatches
			: AobBoundedScanOutcomeKind.HostReportedError;
	}

	// Reads one address per row until the rows or the staging buffer run out. Returns Unknown when the copy completed.
	private static AobBoundedScanOutcomeKind ReadRows(LuaState state, MemoryScanSession session, AobScanBounds bounds,
		Span<Address> staged, ulong count, ref AobBoundedScanFacts facts, CancellationToken cancellationToken,
		out int staging)
	{
		// Rows are addressed with CE's Int32 index; rows beyond it stay unread and make the in-bounds count inexact.
		ulong readable = Math.Min(count, int.MaxValue);
		staging = 0;
		for (int index = 0; (ulong) index < readable && staging < staged.Length; index++)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return AobBoundedScanOutcomeKind.Cancelled;
			}

			MemoryScanRowRead row = session.TryReadAddressRowCore(state, index, out Address address,
				out LuaStatus luaStatus);
			facts.RowsRead++;
			facts.UnreadHostRows = count - facts.RowsRead;
			if (row == MemoryScanRowRead.LuaFailure)
			{
				facts.LuaStatus = FailureStatus(luaStatus);
				return AobBoundedScanOutcomeKind.ScanFailed;
			}

			if (row != MemoryScanRowRead.Read)
			{
				return AobBoundedScanOutcomeKind.InvalidResult;
			}

			// CE's start bound is not byte-exact (spike D4.2): drop and count what begins below it. The stop bound is
			// honoured by CE (D4.1); the second check is defensive.
			if (address < bounds.Start)
			{
				facts.BelowStartSkipped++;
			}
			else if (address >= bounds.Stop)
			{
				facts.AtOrAfterStopSkipped++;
			}
			else
			{
				staged[staging++] = address;
			}
		}

		return cancellationToken.IsCancellationRequested
			? AobBoundedScanOutcomeKind.Cancelled
			: AobBoundedScanOutcomeKind.Unknown;
	}

	// A secondary diagnostic: its failure is recorded and never replaces the primary outcome (binding DoD A.10).
	private static void ReadHostErrorText(LuaState state, MemoryScanSession session, ref AobBoundedScanFacts facts)
	{
		if (!session.TryReadHostErrorTextCore(state, out string? text, out bool truncated))
		{
			facts.IsHostErrorTextUnreadable = true;
			return;
		}

		if (text.Length > 0)
		{
			facts.HostErrorText = text;
			facts.IsHostErrorTextTruncated = truncated;
		}
	}

	private static AobBoundedScanOutcomeKind FromException(MemoryScanException exception,
		ref AobBoundedScanFacts facts)
	{
		switch (exception.FailureKind)
		{
			case MemoryScanFailureKind.TargetIdentityMismatch:
				return AobBoundedScanOutcomeKind.TargetChanged;
			case MemoryScanFailureKind.TargetIdentityUnavailable:
				return AobBoundedScanOutcomeKind.TargetIdentityUnavailable;
			case MemoryScanFailureKind.RuntimeInvalidated:
				return AobBoundedScanOutcomeKind.RuntimeInvalidated;
			case MemoryScanFailureKind.UnexpectedResult:
				return AobBoundedScanOutcomeKind.InvalidResult;
			default:
				facts.LuaStatus = exception.InnerException is LuaException lua
					? FailureStatus(lua.Status)
					: LuaStatus.RuntimeError;
				return AobBoundedScanOutcomeKind.ScanFailed;
		}
	}

	private static AobBoundedScanOutcomeKind FromContext(MemoryScanMaterializationStatus context)
	{
		return context switch
		{
			MemoryScanMaterializationStatus.RuntimeInvalidated => AobBoundedScanOutcomeKind.RuntimeInvalidated,
			MemoryScanMaterializationStatus.TargetIdentityMismatch => AobBoundedScanOutcomeKind.TargetChanged,
			_ => AobBoundedScanOutcomeKind.TargetIdentityUnavailable
		};
	}

	private static AobBoundedScanResult Refused(AobBoundedScanOutcomeKind kind)
	{
		AobBoundedScanFacts facts = new() { Kind = kind, Termination = MemoryScanTerminationStatus.NotRequired };
		return new AobBoundedScanResult(in facts);
	}

	private static LuaStatus FailureStatus(LuaStatus status)
	{
		return status.IsOk ? LuaStatus.RuntimeError : status;
	}

	private static void RequireEnabledMainThread()
	{
		if (!LuaRuntime.IsAttached)
		{
			throw new InvalidOperationException(
				"The Cheat Engine plugin is not enabled, so " + ContextOperation + " cannot acquire its Lua state.");
		}

		if (!LuaRuntime.IsMainThread)
		{
			throw new InvalidOperationException(
				ContextOperation + " must run on Cheat Engine's main thread; it does not dispatch work implicitly.");
		}
	}
}
