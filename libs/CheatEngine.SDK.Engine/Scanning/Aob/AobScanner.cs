using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>Runs Cheat Engine's string-form <c>AOBScan</c> and returns its caller-owned StringList result.</summary>
/// <remarks>
///     <para>
///         The exact CE 7.7.0.10621 name is <c>AOBScan</c>. It returns a StringList of matching addresses, and CE's
///         own documentation says that the caller must free the list. This API exposes that fact as
///         <see cref="Owned{T}" />: dispose it before plugin disable, preferably after copying the wanted strings or
///         addresses into managed storage. The returned <see cref="StringList" /> remains a borrowed handle and has no
///         public destroy member.
///     </para>
///     <para>
///         <see cref="TryScanDetailed(string, out Owned{StringList}?)" /> preserves the distinct outcomes of an
///         unresolved global, protected Lua failure, CE <c>nil</c> (or no value), and a malformed non-nil result.
///         <see cref="TryScanOutcome(string, out Owned{StringList}?)" /> further distinguishes a validated empty
///         StringList from those failures without assigning no-match meaning to raw <c>nil</c>. The boolean
///         <c>TryScan</c> overloads retain their existing convenience contract by returning <see langword="false" /> for
///         all of those outcomes. The stack is restored in every case. A detached runtime throws
///         <see cref="InvalidOperationException" />. CE's documentation does not state AOB scan thread affinity, so this
///         method makes no unverified main-thread claim and uses the calling thread's host Lua state. The owner it
///         returns follows <see cref="Owned{T}" />'s existing main-thread destruction contract.
///     </para>
///     <para>
///         On the pinned profile <c>ce-7.7.0.10621-x64-managed-hostfxr</c>, zero matches are reported as
///         <see cref="AobScanStatus.NoResult" /> / <see cref="AobScanOutcomeKind.NoResult" />: <c>AOBScan</c> returns no
///         value, and an empty <c>StringList</c> was never observed (host observation, spike 2026-09-22; the Q27 C3
///         receipt is still pending). <see cref="AobScanOutcomeKind.NoMatches" /> on this global route stays reserved for
///         a valid empty list and is unreachable on that profile. The SDK keeps <c>NoResult</c> raw because a host
///         failure can produce the same shape; outcome categories come from the result's Lua type and arity, never from
///         Lua error text.
///     </para>
///     <para>
///         Once CE has returned a host list, the SDK holds its only destroy authority until the managed owner exists. A
///         managed failure while publishing that owner (for example an allocation failure) is a lifecycle fault: the SDK
///         destroys the unpublished list once, never retries, discards the status of that one attempt, and rethrows the
///         original exception. No owner escapes and no list is leaked or destroyed twice.
///     </para>
///     <para>
///         The CE primitive is synchronous and this SDK exposes no range/module restriction, result limit, early-stop,
///         or <c>CancellationToken</c> parameter for it: none is a verified <c>AOBScan</c> execution control. A Client
///         may decide whether to admit or wait for work and may cap strings after copying them, but neither action
///         interrupts a running CE scan or proves a bound on CE work. Copy every needed string while the returned owner
///         is alive, then dispose that owner exactly once.
///     </para>
/// </remarks>
public static class AobScanner
{
	private static readonly LuaRef SAobScan = new();

	/// <summary>Runs AOBScan with only its required pattern argument.</summary>
	/// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
	/// <param name="results">The caller-owned result list, or <see langword="null" /> on failure/no result.</param>
	/// <returns><see langword="true" /> when CE returned a non-null host object.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     A managed failure while publishing the owner is a lifecycle fault: the SDK destroys the unpublished list once
	///     and rethrows the original exception.
	/// </remarks>
	[RequiresPluginEnabled]
	public static bool TryScan(string pattern, [NotNullWhen(true)] out Owned<StringList>? results)
	{
		return TryScanDetailed(pattern, out results) == AobScanStatus.Success;
	}

	/// <summary>Runs AOBScan with explicit protection and alignment options.</summary>
	/// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
	/// <param name="options">The optional CE arguments and their exact positions.</param>
	/// <param name="results">The caller-owned result list, or <see langword="null" /> on failure/no result.</param>
	/// <returns><see langword="true" /> when CE returned a non-null host object.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     A managed failure while publishing the owner is a lifecycle fault: the SDK destroys the unpublished list once
	///     and rethrows the original exception.
	/// </remarks>
	[RequiresPluginEnabled]
	public static bool TryScan(string pattern, AobScanOptions options,
		[NotNullWhen(true)] out Owned<StringList>? results)
	{
		return TryScanDetailed(pattern, options, out results) == AobScanStatus.Success;
	}

	/// <summary>Runs AOBScan with only its required pattern argument and reports its precise result category.</summary>
	/// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
	/// <param name="results">
	///     The caller-owned result list only when the returned status is
	///     <see cref="AobScanStatus.Success" />.
	/// </param>
	/// <returns>The protected AOBScan outcome without parsing a Lua error message.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     A managed failure while publishing the owner is a lifecycle fault: the SDK destroys the unpublished list once
	///     and rethrows the original exception.
	/// </remarks>
	[RequiresPluginEnabled]
	public static AobScanStatus TryScanDetailed(string pattern, out Owned<StringList>? results)
	{
		return TryScanDetailed(pattern, AobScanOptions.Default, out results);
	}

	/// <summary>Runs AOBScan with explicit protection and alignment options and reports its precise result category.</summary>
	/// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
	/// <param name="options">The optional CE arguments and their exact positions.</param>
	/// <param name="results">
	///     The caller-owned result list only when the returned status is
	///     <see cref="AobScanStatus.Success" />.
	/// </param>
	/// <returns>The protected AOBScan outcome without parsing a Lua error message.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     A managed failure while publishing the owner is a lifecycle fault: the SDK destroys the unpublished list once
	///     and rethrows the original exception.
	/// </remarks>
	[RequiresPluginEnabled]
	public static AobScanStatus TryScanDetailed(string pattern, AobScanOptions options,
		out Owned<StringList>? results)
	{
		return TryScanDetailedCore(pattern, options, PublishResultList, out results);
	}

	/// <summary>Runs AOBScan and reports whether a valid returned StringList contains matches.</summary>
	/// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
	/// <param name="results">
	///     The caller-owned list when <see cref="AobScanOutcome.IsSuccess" /> is <see langword="true" />; otherwise
	///     <see langword="null" />. Copy required entries before disposing the owner exactly once.
	/// </param>
	/// <returns>
	///     A factual outcome that classifies no matches only from a valid StringList with count zero. Raw Lua
	///     <c>nil</c> (or no value), unavailable globals, protected Lua failures, malformed return values, and unreadable
	///     counts remain distinct. On the pinned CE 7.7.0.10621 x64 profile, a scan with zero matches returns no value and
	///     is therefore reported as <see cref="AobScanOutcomeKind.NoResult" />, never
	///     <see cref="AobScanOutcomeKind.NoMatches" /> (host observation, spike 2026-09-22; Q27 C3 pending).
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     A managed failure while publishing the owner is a lifecycle fault: the SDK destroys the unpublished list once
	///     and rethrows the original exception.
	/// </remarks>
	[RequiresPluginEnabled]
	public static AobScanOutcome TryScanOutcome(string pattern, out Owned<StringList>? results)
	{
		return TryScanOutcome(pattern, AobScanOptions.Default, out results);
	}

	/// <summary>Runs AOBScan with explicit CE protection/alignment options and reports a structured result.</summary>
	/// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
	/// <param name="options">The optional CE arguments and their exact positions.</param>
	/// <param name="results">
	///     The caller-owned list when <see cref="AobScanOutcome.IsSuccess" /> is <see langword="true" />; otherwise
	///     <see langword="null" />. Copy required entries before disposing the owner exactly once.
	/// </param>
	/// <returns>
	///     The factual protected AOB result, including a valid empty-list no-match classification. On the pinned
	///     CE 7.7.0.10621 x64 profile, zero matches are reported as <see cref="AobScanOutcomeKind.NoResult" />.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     A managed failure while publishing the owner is a lifecycle fault: the SDK destroys the unpublished list once
	///     and rethrows the original exception.
	/// </remarks>
	[RequiresPluginEnabled]
	public static AobScanOutcome TryScanOutcome(string pattern, AobScanOptions options,
		out Owned<StringList>? results)
	{
		return TryScanOutcomeCore(pattern, options, PublishResultList, out results);
	}

	// Test seam: the same protected call and classification as TryScanDetailed, with a substitutable owner
	// publication. Production callers always pass PublishResultList.
	internal static AobScanStatus TryScanDetailedCore(string pattern, AobScanOptions options,
		AobResultListPublisher publisher, out Owned<StringList>? results)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(publisher);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		return TryScanCore(state, pattern, options, publisher, out results, out _);
	}

	// Test seam: the same protected call and classification as TryScanOutcome, with a substitutable owner
	// publication. Production callers always pass PublishResultList.
	internal static AobScanOutcome TryScanOutcomeCore(string pattern, AobScanOptions options,
		AobResultListPublisher publisher, out Owned<StringList>? results)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(publisher);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		AobScanStatus status = TryScanCore(state, pattern, options, publisher, out results, out LuaStatus luaStatus);
		return Classify(status, luaStatus, ref results);
	}

	// Every member has its own arm. Unknown (the default) and Success (which never reaches this mapping, because a
	// successful call is classified from its list count) map to the Unknown outcome, never to a count failure: an
	// unexpected status must not be reported as a host list that was returned but could not be counted.
	internal static AobScanOutcome FromStatus(AobScanStatus status, LuaStatus luaStatus)
	{
		return status switch
		{
			AobScanStatus.Unknown => default,
			AobScanStatus.Success => default,
			AobScanStatus.GlobalUnavailable => AobScanOutcome.GlobalUnavailable,
			AobScanStatus.LuaFailure => AobScanOutcome.ProtectedLuaFailure(ToFailureStatus(luaStatus)),
			AobScanStatus.NoResult => AobScanOutcome.NoResult,
			AobScanStatus.InvalidResult => AobScanOutcome.InvalidResult,
			_ => default
		};
	}

	// Classifies a completed protected call. A published owner whose count cannot be read has no caller left to
	// release it, so it is disposed here, once, before the failure outcome is returned.
	private static AobScanOutcome Classify(AobScanStatus status, LuaStatus luaStatus,
		ref Owned<StringList>? results)
	{
		if (status != AobScanStatus.Success)
		{
			return FromStatus(status, luaStatus);
		}

		Owned<StringList> owned = results!;
		try
		{
			if (!owned.Value.TryGetCount(out int resultCount) || resultCount < 0)
			{
				owned.Dispose();
				results = null;
				return AobScanOutcome.ResultListCountUnavailable;
			}

			return resultCount == 0 ? AobScanOutcome.NoMatches : AobScanOutcome.Matches(resultCount);
		}
		catch (LuaException exception)
		{
			owned.Dispose();
			results = null;
			return AobScanOutcome.ProtectedLuaFailure(ToFailureStatus(exception.Status));
		}
	}

	private static AobScanStatus TryScanCore(LuaState state, string pattern, AobScanOptions options,
		AobResultListPublisher publisher, out Owned<StringList>? results, out LuaStatus luaStatus)
	{
		results = null;
		luaStatus = LuaStatus.Ok;
		CEObject unpublished = CEObject.Null;
		try
		{
			LuaGlobalPushOutcome global = LuaGlobalFunctions.TryPushWithOutcome(state, SAobScan, "AOBScan"u8);
			if (global.Status == LuaGlobalPushStatus.Unavailable)
			{
				return AobScanStatus.GlobalUnavailable;
			}

			if (!global.IsSuccess)
			{
				luaStatus = ToFailureStatus(global.LuaStatus);
				return AobScanStatus.LuaFailure;
			}

			int argumentCount = PushArguments(state, pattern, options);
			luaStatus = state.TryCall(argumentCount, 1);
			if (!luaStatus.IsOk)
			{
				return AobScanStatus.LuaFailure;
			}

			// CE 7.7.0.10621 returns no value on zero matches; the one-result call reads that as nil (spike D1).
			if (state.IsNil(-1))
			{
				return AobScanStatus.NoResult;
			}

			if (!CEObject.TryRead(state, -1, out CEObject handle))
			{
				return AobScanStatus.InvalidResult;
			}

			// From here until the owner exists, this frame holds the list's only destroy authority.
			unpublished = handle;
			results = publisher(StringList.FromHandle(handle));
			unpublished = CEObject.Null;
			return AobScanStatus.Success;
		}
		catch (LuaException exception) when (unpublished.IsNull)
		{
			// A protected failure before CE returned a list. A failure while publishing the owner is not caught here: it
			// is a lifecycle fault and propagates unchanged after the single rollback below.
			results = null;
			luaStatus = ToFailureStatus(exception.Status);
			return AobScanStatus.LuaFailure;
		}
		finally
		{
			if (!unpublished.IsNull)
			{
				RollBackUnpublishedList(state, unpublished);
			}
		}
	}

	// One destroy attempt for a host list whose managed owner could not be published. It is never retried: a failed
	// protected destroy may already have freed part of the object. Its status is discarded and any exception it throws
	// is swallowed, so the original publication failure is the exception the caller observes.
	private static void RollBackUnpublishedList(LuaState state, CEObject unpublished)
	{
		try
		{
			using LuaFrame rollback = new(state);
			_ = unpublished.TryDestroy(state);
		}
		catch (Exception)
		{
			// Deliberately ignored: see the method comment.
		}
	}

	private static Owned<StringList> PublishResultList(StringList list)
	{
		return new Owned<StringList>(list);
	}

	private static LuaStatus ToFailureStatus(LuaStatus luaStatus)
	{
		return luaStatus.IsOk ? LuaStatus.RuntimeError : luaStatus;
	}

	private static int PushArguments(LuaState state, string pattern, AobScanOptions options)
	{
		StringMarshaller.Push(state, pattern);
		if (options.HasAlignment)
		{
			StringMarshaller.Push(state, options.ProtectionFlags);
			Int32Marshaller.Push(state, (int) options.AlignmentMethod);
			StringMarshaller.Push(state, options.AlignmentParameter);
			return 4;
		}

		if (options.ProtectionFlags is null)
		{
			return 1;
		}

		StringMarshaller.Push(state, options.ProtectionFlags);
		return 2;
	}
}
