using System;
using System.Diagnostics;
using System.Threading;

using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>
///     The first-found AOB route of <see cref="AobScanner" />: one MemScan/FoundList session with <c>OnlyOneResult</c>
///     on, a byte-array first scan over <c>[Start, Stop)</c>, a wait, and one <c>getOnlyResult</c> read. The found list is
///     never initialized (CE documents that the one-result mode does not fill it). Every exit releases the session once.
/// </summary>
internal static class AobFirstFoundScan
{
	private const string ContextOperation = "AobScanner.TryFindFirstFoundWithinBounds";

	internal static AobFirstFoundResult Run(string pattern, AobScanBounds bounds, AobScanOptions options,
		CancellationToken cancellationToken)
	{
		if (!bounds.IsValid)
		{
			return Refused(AobFirstFoundOutcomeKind.InvalidBounds);
		}

		RequireEnabledMainThread();
		if (cancellationToken.IsCancellationRequested)
		{
			return Refused(AobFirstFoundOutcomeKind.Cancelled);
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		MemoryScanCreationOutcome creation = MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? session);
		if (creation.Status != MemoryScanCreationStatus.Success || session is null)
		{
			return new AobFirstFoundResult(AobFirstFoundOutcomeKind.SessionCreationFailed, Address.Zero, creation,
				LuaStatus.Ok, TimeSpan.Zero, default);
		}

		AobFirstFoundOutcomeKind kind;
		Address address = Address.Zero;
		LuaStatus luaStatus = LuaStatus.Ok;
		TimeSpan hostScanElapsed = TimeSpan.Zero;
		MemoryScanReleaseOutcome release;
		try
		{
			kind = Execute(state, session, pattern, bounds, options, cancellationToken, out address, out luaStatus,
				out hostScanElapsed);
		}
		catch (MemoryScanException exception)
		{
			kind = FromException(exception, out luaStatus);
		}
		finally
		{
			release = session.ReleaseWithOutcome();
		}

		if (kind is not (AobFirstFoundOutcomeKind.Found or AobFirstFoundOutcomeKind.FoundOutsideBounds))
		{
			address = Address.Zero;
		}

		return new AobFirstFoundResult(kind, address, creation, luaStatus, hostScanElapsed, release);
	}

	private static AobFirstFoundOutcomeKind Execute(LuaState state, MemoryScanSession session, string pattern,
		AobScanBounds bounds, AobScanOptions options, CancellationToken cancellationToken, out Address address,
		out LuaStatus luaStatus, out TimeSpan hostScanElapsed)
	{
		address = Address.Zero;
		hostScanElapsed = TimeSpan.Zero;
		luaStatus = session.SetOnlyOneResultCore(state, true);
		if (!luaStatus.IsOk)
		{
			luaStatus = FailureStatus(luaStatus);
			return AobFirstFoundOutcomeKind.ScanFailed;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return AobFirstFoundOutcomeKind.Cancelled;
		}

		FirstScanRequest request = FirstScanRequest.ByteArray(pattern, bounds.Start, bounds.Stop,
			options.ProtectionFlags ?? string.Empty, options.AlignmentMethod,
			options.AlignmentParameter ?? string.Empty);
		long scanStarted = Stopwatch.GetTimestamp();
		session.StartFirstScan(in request);
		MemoryScanMaterializationStatus context = session.TryEnsureCurrentContextCore(state);
		if (context != MemoryScanMaterializationStatus.Success)
		{
			return FromContext(context);
		}

		// The one-result mode does not fill the found list: never initialize it (celua.txt line 2656).
		MemoryScanWaitStatus waited = session.WaitCore(state, null, false, out luaStatus);
		hostScanElapsed = Stopwatch.GetElapsedTime(scanStarted);
		if (waited != MemoryScanWaitStatus.Completed)
		{
			luaStatus = FailureStatus(luaStatus);
			return AobFirstFoundOutcomeKind.ScanFailed;
		}

		context = session.TryEnsureCurrentContextCore(state);
		if (context != MemoryScanMaterializationStatus.Success)
		{
			return FromContext(context);
		}

		return Classify(session.TryReadOnlyResultCore(state, out address, out luaStatus), bounds, address,
			ref luaStatus);
	}

	private static AobFirstFoundOutcomeKind Classify(MemoryScanOnlyResult read, AobScanBounds bounds, Address address,
		ref LuaStatus luaStatus)
	{
		switch (read)
		{
			case MemoryScanOnlyResult.Found:
				// A match below Start is indeterminate: CE's start bound is not byte-exact (spike D4.2), so it may have
				// stopped on a match just before the range; whether an in-bounds match exists is unknown.
				return bounds.Contains(address)
					? AobFirstFoundOutcomeKind.Found
					: AobFirstFoundOutcomeKind.FoundOutsideBounds;
			case MemoryScanOnlyResult.NotFound:
				return AobFirstFoundOutcomeKind.NotFound;
			case MemoryScanOnlyResult.LuaFailure:
				luaStatus = FailureStatus(luaStatus);
				return AobFirstFoundOutcomeKind.ScanFailed;
			default:
				return AobFirstFoundOutcomeKind.InvalidResult;
		}
	}

	private static AobFirstFoundOutcomeKind FromException(MemoryScanException exception, out LuaStatus luaStatus)
	{
		luaStatus = LuaStatus.Ok;
		switch (exception.FailureKind)
		{
			case MemoryScanFailureKind.TargetIdentityMismatch:
				return AobFirstFoundOutcomeKind.TargetChanged;
			case MemoryScanFailureKind.TargetIdentityUnavailable:
				return AobFirstFoundOutcomeKind.TargetIdentityUnavailable;
			case MemoryScanFailureKind.RuntimeInvalidated:
				return AobFirstFoundOutcomeKind.RuntimeInvalidated;
			case MemoryScanFailureKind.UnexpectedResult:
				return AobFirstFoundOutcomeKind.InvalidResult;
			default:
				luaStatus = exception.InnerException is LuaException lua
					? FailureStatus(lua.Status)
					: LuaStatus.RuntimeError;
				return AobFirstFoundOutcomeKind.ScanFailed;
		}
	}

	private static AobFirstFoundOutcomeKind FromContext(MemoryScanMaterializationStatus context)
	{
		return context switch
		{
			MemoryScanMaterializationStatus.RuntimeInvalidated => AobFirstFoundOutcomeKind.RuntimeInvalidated,
			MemoryScanMaterializationStatus.TargetIdentityMismatch => AobFirstFoundOutcomeKind.TargetChanged,
			_ => AobFirstFoundOutcomeKind.TargetIdentityUnavailable
		};
	}

	private static AobFirstFoundResult Refused(AobFirstFoundOutcomeKind kind)
	{
		return new AobFirstFoundResult(kind, Address.Zero, default, LuaStatus.Ok, TimeSpan.Zero, default);
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
