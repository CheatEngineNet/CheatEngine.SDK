using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
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
///         The global <c>AOBScan</c> primitive is synchronous and unbounded: this SDK exposes no range/module restriction,
///         result limit, early-stop, or <c>CancellationToken</c> parameter for it, because none is a verified
///         <c>AOBScan</c> execution control. Its native cost is a scan of the whole address space whatever a caller
///         filters afterwards. A Client may cap strings after copying them, but that neither interrupts a running CE scan
///         nor bounds CE work. Copy every needed string while the returned owner is alive, then dispose that owner exactly
///         once.
///     </para>
///     <para>
///         The bounded route is
///         <see cref="TryScanWithinBounds(string, AobScanBounds, AobScanOptions, Span{Address}, CancellationToken)" />: a
///         MemScan byte-array scan whose CE work is limited to <c>[Start, Stop)</c>, exhaustive (never a first match),
///         with an address-only copy. Its four limits are distinct: the CE work limit (<see cref="AobScanBounds" />), the
///         available results (<see cref="AobBoundedScanResult.HostResultCount" />), the materialization limit (the
///         destination length, <see cref="AobBoundedScanResult.IsMaterializationLimitReached" />) and the call deadline
///         (the optional wait timeout, <see cref="AobBoundedScanOutcomeKind.WaitTimedOut" />). Uniqueness needs an
///         exhausted domain or a second in-bounds match, never a first-found scan.
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

	/// <summary>
	///     Runs AOBScan with explicit CE protection/alignment options, reports a structured result, and reports the
	///     target observations made immediately before and after the call.
	/// </summary>
	/// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
	/// <param name="options">The optional CE arguments and their exact positions.</param>
	/// <param name="results">
	///     The caller-owned list when <see cref="AobScanOutcome.IsSuccess" /> is <see langword="true" />; otherwise
	///     <see langword="null" />. Copy required entries before disposing the owner exactly once.
	/// </param>
	/// <param name="targetContext">
	///     The Cheat Engine target selection observed immediately before and after the <c>AOBScan</c> call, within the
	///     same admitted Lua operation.
	/// </param>
	/// <returns>
	///     The same factual outcome as <see cref="TryScanOutcome(string, AobScanOptions, out Owned{StringList}?)" />
	///     for the same host response. The target observations never refuse the scan and never change its kind.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     <para>
	///         <c>AOBScan</c> scans CE's current selection. When
	///         <see cref="AobScanTargetContext.IsSameQualifiedIncarnation" /> is <see langword="false" />, the returned
	///         addresses may belong to another target (the selection changed during the call, or it could not be qualified
	///         before or after it); the caller decides what to do with them.
	///     </para>
	///     <para>
	///         A managed failure while publishing the owner is a lifecycle fault: the SDK destroys the unpublished list
	///         once and rethrows the original exception.
	///     </para>
	/// </remarks>
	[RequiresPluginEnabled]
	public static AobScanOutcome TryScanOutcome(string pattern, AobScanOptions options,
		out Owned<StringList>? results, out AobScanTargetContext targetContext)
	{
		return TryScanOutcomeCore(pattern, options, PublishResultList, out results, out targetContext);
	}

	/// <summary>
	///     Runs an exhaustive AOB scan whose CE work is bounded by <paramref name="bounds" />, through a MemScan session,
	///     and copies the in-bounds match addresses into <paramref name="destination" />.
	/// </summary>
	/// <param name="pattern">CE's byte-array pattern text, passed without normalization.</param>
	/// <param name="bounds">
	///     The CE work limit <c>[Start, Stop)</c>. An invalid value (the default) is refused before any CE call with
	///     <see cref="AobBoundedScanOutcomeKind.InvalidBounds" />.
	/// </param>
	/// <param name="options">
	///     CE's protection and alignment arguments. A <see langword="null" /> protection string is passed as CE's "find
	///     everything" empty string.
	/// </param>
	/// <param name="destination">
	///     The materialization limit: in-bounds addresses are written in CE's found-list order until it is full. It is
	///     written only for <see cref="AobBoundedScanOutcomeKind.Matches" /> and
	///     <see cref="AobBoundedScanOutcomeKind.NoMatches" />; any other outcome leaves it unchanged.
	/// </param>
	/// <param name="cancellationToken">
	///     Observed before the session is created, before the scan starts, after it completes and between row reads; it
	///     cannot interrupt a CE call already running. A cancelled scan publishes nothing and still releases its session.
	/// </param>
	/// <returns>The factual outcome, the four limits it met, the copy accounting and the separate durations.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="destination" /> is empty.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	/// <remarks>
	///     <para>
	///         Unlike the global <c>AOBScan</c> route, the CE work itself is bounded: the session's MemScan scans only
	///         <c>[Start, Stop)</c> with a hexadecimal byte-array first scan
	///         (<see cref="FirstScanRequest.ByteArray(string, Address, Address, string, Enums.FastScanMethod, string)" />).
	///         The scan is exhaustive: <c>OnlyOneResult</c> is always switched off before it starts, and <c>IsUnique</c>,
	///         <c>Result</c> and the scan callbacks are never used. On the pinned profile
	///         <c>ce-7.7.0.10621-x64-managed-hostfxr</c> this route returned exactly the in-module subset of the global
	///         route, in the same order (Lua-only host observation, spike 2026-09-22, D4.5; the Q28 C3 receipt is still
	///         pending). CE's start bound is not byte-exact, so addresses below <see cref="AobScanBounds.Start" /> are
	///         dropped and counted (<see cref="AobBoundedScanResult.BelowStartSkipped" />).
	///     </para>
	///     <para>
	///         Zero in-bounds matches with an empty CE error text is the factual
	///         <see cref="AobBoundedScanOutcomeKind.NoMatches" />; with a non-empty text it is
	///         <see cref="AobBoundedScanOutcomeKind.HostReportedError" />, the text being copied but never parsed. Only the
	///         addresses are read (one <c>getAddress</c> call per needed row, never <c>getValue</c>), and reading stops as
	///         soon as the destination is full. Order is CE's found-list order, which CE does not document: never infer the
	///         lowest address from the first element. Uniqueness needs an exhausted domain
	///         (<see cref="AobBoundedScanResult.InBoundsCountIsExact" />) or a second in-bounds match (a destination of at
	///         least two elements and <see cref="AobBoundedScanResult.Written" /> of two or more).
	///     </para>
	///     <para>
	///         The call blocks CE's main thread for the scan, the copy and the release, and waits through CE's
	///         no-timeout wait. The session is released once, child before parent, on every exit.
	///     </para>
	///     <para>
	///         Addresses are staged in a pooled buffer as long as <paramref name="destination" /> and copied out only on
	///         success, so the call's managed memory peak is about twice the materialization limit, even for zero
	///         matches. A managed failure to obtain that buffer (for example a destination too large for one array) is a
	///         lifecycle fault: the session is still released once, child before parent, and the original exception
	///         propagates.
	///     </para>
	/// </remarks>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public static AobBoundedScanResult TryScanWithinBounds(string pattern, AobScanBounds bounds, AobScanOptions options,
		Span<Address> destination, CancellationToken cancellationToken)
	{
		ValidateBoundedArguments(pattern, destination);
		return AobBoundedScan.Run(pattern, bounds, options, null, destination, cancellationToken);
	}

	/// <summary>
	///     Runs the bounded, exhaustive AOB scan of
	///     <see cref="TryScanWithinBounds(string, AobScanBounds, AobScanOptions, Span{Address}, CancellationToken)" />
	///     with a call deadline on CE's wait.
	/// </summary>
	/// <param name="pattern">CE's byte-array pattern text, passed without normalization.</param>
	/// <param name="bounds">The CE work limit <c>[Start, Stop)</c>; an invalid value is refused before any CE call.</param>
	/// <param name="options">CE's protection and alignment arguments.</param>
	/// <param name="waitTimeout">
	///     The call deadline of CE's <c>waitTillDone(timeout)</c>: strictly positive and at most
	///     <see cref="int.MaxValue" /> milliseconds; a sub-millisecond value rounds up to one millisecond.
	/// </param>
	/// <param name="destination">The materialization limit; written only for a successful outcome.</param>
	/// <param name="cancellationToken">Observed between CE calls; it cannot interrupt a CE call already running.</param>
	/// <returns>The factual outcome, the four limits it met, the copy accounting and the separate durations.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="destination" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="waitTimeout" /> is outside the accepted range.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	/// <remarks>
	///     When the deadline expires first the outcome is <see cref="AobBoundedScanOutcomeKind.WaitTimedOut" />: the SDK
	///     requests one cooperative stop (<c>terminateScan(false)</c>, then a five-second settle wait), reports it in
	///     <see cref="AobBoundedScanResult.Termination" />, publishes nothing and releases the session without repeating
	///     the stop. The timed-out wait and <c>terminateScan</c> were not observed on the pinned CE 7.7.0.10621 host
	///     (spike D4.7), which is why this overload is experimental.
	/// </remarks>
	[Experimental("CESDK5010", UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md")]
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public static AobBoundedScanResult TryScanWithinBounds(string pattern, AobScanBounds bounds, AobScanOptions options,
		TimeSpan waitTimeout, Span<Address> destination, CancellationToken cancellationToken)
	{
		ValidateBoundedArguments(pattern, destination);
		int milliseconds = MemoryScanSession.ToWaitMilliseconds(waitTimeout, nameof(waitTimeout));
		return AobBoundedScan.Run(pattern, bounds, options, milliseconds, destination, cancellationToken);
	}

	/// <summary>
	///     Runs CE's one-result MemScan mode over <paramref name="bounds" /> and reports the first match CE found:
	///     first found, order unspecified, never a uniqueness proof.
	/// </summary>
	/// <param name="pattern">CE's byte-array pattern text, passed without normalization.</param>
	/// <param name="bounds">The CE work limit <c>[Start, Stop)</c>; an invalid value is refused before any CE call.</param>
	/// <param name="options">CE's protection and alignment arguments; a <see langword="null" /> protection string means "find everything".</param>
	/// <param name="cancellationToken">
	///     Observed before the session is created and before the scan starts; it cannot interrupt the scan.
	/// </param>
	/// <returns>
	///     <see cref="AobFirstFoundOutcomeKind.Found" /> with some in-bounds match,
	///     <see cref="AobFirstFoundOutcomeKind.NotFound" />, the indeterminate
	///     <see cref="AobFirstFoundOutcomeKind.FoundOutsideBounds" />, or a failure; the session is released once.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	/// <remarks>
	///     <para>
	///         CE stops at the first match it finds and exposes it through <c>getOnlyResult</c> (<c>celua.txt</c> lines
	///         2656-2657); CE documents no order. The C3 spike saw the lowest in-module address three times out of three,
	///         which is an observation, not a contract. The result must never back a bounded or range scan, a "require
	///         single" query, or any exhaustive query: use
	///         <see cref="TryScanWithinBounds(string, AobScanBounds, AobScanOptions, Span{Address}, CancellationToken)" />,
	///         which is exhaustive, for those.
	///     </para>
	///     <para>
	///         The session's found list is never initialized, because CE documents that the one-result mode does not fill
	///         it. A match reported below <see cref="AobScanBounds.Start" /> is
	///         <see cref="AobFirstFoundOutcomeKind.FoundOutsideBounds" />: CE's start bound is not byte-exact, so whether an
	///         in-bounds match exists is unknown. The <c>getOnlyResult</c> no-match path and this route's call sequence
	///         were not observed on the pinned CE 7.7.0.10621 host, which is why it is experimental (CESDK5011).
	///     </para>
	/// </remarks>
	[Experimental("CESDK5011", UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md")]
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public static AobFirstFoundResult TryFindFirstFoundWithinBounds(string pattern, AobScanBounds bounds,
		AobScanOptions options, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		return AobFirstFoundScan.Run(pattern, bounds, options, cancellationToken);
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

	// The target-context variant: the observations bracket the protected AOBScan call inside the same admitted
	// operation. They are facts only; the classification is exactly the one of the variant above.
	internal static AobScanOutcome TryScanOutcomeCore(string pattern, AobScanOptions options,
		AobResultListPublisher publisher, out Owned<StringList>? results, out AobScanTargetContext targetContext)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(publisher);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		TargetSelectionObservation before = ObserveTarget(state);
		AobScanStatus status = TryScanCore(state, pattern, options, publisher, out results, out LuaStatus luaStatus);
		TargetSelectionObservation after = ObserveTarget(state);
		targetContext = new AobScanTargetContext(before, after);
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

	private static void ValidateBoundedArguments(string pattern, Span<Address> destination)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		if (destination.IsEmpty)
		{
			throw new ArgumentException(
				"A bounded AOB scan needs a non-empty destination: its length is the materialization limit.",
				nameof(destination));
		}
	}

	private static TargetSelectionObservation ObserveTarget(LuaState state)
	{
		int top = state.Top;
		try
		{
			return TargetSelection.ObserveCurrent(state);
		}
		finally
		{
			state.SetTop(top);
		}
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
