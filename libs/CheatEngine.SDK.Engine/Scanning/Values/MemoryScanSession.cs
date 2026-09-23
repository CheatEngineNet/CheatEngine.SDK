using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     A stateful, main-thread-only owner of one explicitly owned <see cref="MemScan" /> and its explicitly owned child
///     <see cref="FoundList" />. It serializes the CE sequence needed to scan and read results safely.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="MemoryScanSessions.TryCreate" /> is the normal production constructor. It creates the
///         <c>MemScan</c> parent and <c>FoundList</c> child through CE's factories and establishes their ownership before
///         exposing this state machine. <see cref="Adopt" /> remains for a separate SDK binding whose ownership proof is
///         equally explicit; consumers cannot manufacture an <see cref="Owned{T}" /> from a borrowed handle.
///     </para>
///     <para>
///         A session accepts <c>firstScan</c> only from <see cref="MemoryScanState.New" />, <c>nextScan</c> only from
///         <see cref="MemoryScanState.ResultsReady" />, and reads only after <c>waitTillDone</c> completed successfully
///         followed
///         by <c>FoundList.initialize</c>. It uses <c>FoundList.deinitialize</c> before its own next scan and reset to
///         release the readable view; this is a session invariant, not a claim that CE rejects every other raw sequence.
///     </para>
///     <para>
///         Dispose is idempotent. When a scan may still be running (a first or next scan was started and no wait,
///         confirmed stop or reset has ended it), it first asks CE for a cooperative stop, <c>terminateScan(false)</c>,
///         and waits for it with <c>waitTillDone(5000)</c>, each once. It then releases a ready found list, destroys the
///         found-list child and finally destroys the scanner parent, each exactly once, even when the stop was not
///         confirmed: CE's own destroy of a scanner stops and waits for its scan controller rather than freeing memory
///         under a running scan thread (ObservedSource: cheat-engine/cheat-engine@ec45d5f
///         <c>Cheat Engine/memscan.pas</c> lines 7942-7946 and 8929-8955). Disposing a scanning session therefore blocks
///         CE's main thread for at most the five-second settle wait plus whatever CE's destroy waits; the stop is
///         cooperative and never forced. It must run while the plugin is enabled and on the Cheat Engine main thread so
///         the contained <see cref="Owned{T}" /> values can reach <c>destroy()</c>. As with <see cref="Owned{T}" />, no
///         finalizer runs native cleanup from an arbitrary thread.
///     </para>
///     <para>
///         CE's waits can run queued main-thread work before they return: on the main thread, <c>waitTillDone</c> pumps
///         <c>CheckSynchronize</c> (ObservedSource: cheat-engine/cheat-engine@ec45d5f
///         <c>Cheat Engine/LuaMemscan.pas</c> lines 145-157), and <c>newScan</c> and <c>destroy</c> wait for CE's scan
///         threads (<c>Cheat Engine/memscan.pas</c> lines 8360-8372 and 8929-8955), which may do the same. Work queued
///         through CE's <c>synchronize</c> (for example with <c>MainThread.Invoke</c>) can therefore call back into this
///         session from inside one of its own CE calls. While one member of the session is inside a CE call, every
///         other member is refused with a <see cref="MemoryScanStateException" />, and <see cref="Dispose" />,
///         <see cref="ReleaseWithOutcome" /> and <see cref="Abandon" /> make no CE call: the one release (or abandon) is
///         deferred until that CE call has returned and then runs exactly once, so no destroy ever runs while a CE call
///         on the same objects is still on the stack. A start, wait, reset or stop interrupted this way makes no
///         further CE call and then throws <see cref="ObjectDisposedException" /> instead of completing its
///         transition.
///     </para>
///     <para>
///         <see cref="WaitForCompletion" /> uses CE's no-timeout <c>waitTillDone()</c> form. CE 7.7.0.10621 also has a
///         timeout form returning a boolean (<c>celua.txt</c> line 2649) and a cooperative <c>terminateScan</c> (line
///         2566); <see cref="TryWaitForCompletion" /> and <see cref="TryTerminateScan" /> project them. Their
///         timed-out and termination paths were not observed on the pinned host (spike D4.7), so both members are
///         <c>[Experimental("CESDK5010")]</c> until a Q29 C3 receipt observes them. The SDK never forces termination: a
///         forced stop can kill CE's scan thread and open a modal dialog on CE's main thread.
///         <see cref="TryGetHostErrorText" /> copies CE's <c>ErrorString</c> as a bounded, unparsed fact; no outcome
///         category is ever derived from it.
///     </para>
/// </remarks>
public sealed class MemoryScanSession : IDisposable
{
	// These are public exception identifiers. Keep CE's exact member names at the private call sites instead.
	private const string FirstScanOperation = "MemoryScan.FirstScan";
	private const string NextScanOperation = "MemoryScan.NextScan";
	private const string WaitForCompletionOperation = "MemoryScan.WaitForCompletion";
	private const string InitializeResultsOperation = "MemoryScan.InitializeResults";
	private const string DeinitializeResultsOperation = "MemoryScan.DeinitializeResults";
	private const string ResetOperation = "MemoryScan.Reset";
	private const string ResultCountOperation = "MemoryScan.ResultCount";
	private const string ResultAddressOperation = "MemoryScan.ResultAddress";
	private const string ResultValueOperation = "MemoryScan.ResultValue";

	/// <summary>The maximum number of UTF-8 bytes that <see cref="TryGetHostErrorText" /> copies from CE.</summary>
	public const int HostErrorTextMaximumUtf8Bytes = 1024;

	/// <summary>
	///     The bounded wait, in milliseconds, after the one cooperative stop request that release and the bounded AOB
	///     route issue for a scan that may still be running.
	/// </summary>
	internal const int ReleaseTerminationWaitMilliseconds = 5000;

	// The member whose CE calls are in progress, or null, and whether that member is the release itself. CE's waits,
	// resets and destroys can run queued main-thread work that calls back into this session (see the type remarks).
	// While a member is active, every other member is refused, and a release or abandon requested meanwhile is
	// recorded here and run by the active member once its CE calls have returned.
	private string? _activeOperation;
	private DeferredDisposal _deferredDisposal;
	private Owned<FoundList>? _foundList;
	private bool _isBound;
	private bool _releaseInProgress;

	// True from immediately before a firstScan/nextScan call (CE may have started work even if the call then fails)
	// until a wait reports completion, a cooperative stop is confirmed, or a reset succeeds.
	private bool _scanMayBeRunning;
	private Owned<MemScan>? _scanner;
	private TargetSelectionObservation _targetObservation;
	private MemoryScanTerminationStatus _termination;

	// The one cooperative stop request of the current scan, if any: it is never repeated, and its status is what a
	// later release reports when the stop stayed unconfirmed.
	private bool _terminationAttempted;

	private MemoryScanSession(Owned<MemScan> scanner, Owned<FoundList> foundList)
	{
		_scanner = scanner;
		_foundList = foundList;
		State = MemoryScanState.New;
	}

	/// <summary>Gets the session's conservative, managed state.</summary>
	public MemoryScanState State
	{
		get;
		private set;
	}

	/// <summary>Gets the attach epoch and state generation that own this session's CE objects.</summary>
	/// <remarks>
	///     A differing current identity means that this session's raw CE handles must never be used through the new Lua
	///     universe. The value is diagnostic data; callers cannot manufacture a matching identity.
	/// </remarks>
	public LuaStateIdentity RuntimeIdentity
	{
		get;
		private set;
	}

	/// <summary>Gets the target observation captured before this session's factory/adoption publication.</summary>
	/// <remarks>
	///     An unqualified observation is retained as evidence rather than replaced with a guessed target. Such a session
	///     cannot begin scan or result operations until a factory/adoption path has captured a qualified incarnation.
	/// </remarks>
	public TargetSelectionObservation TargetObservation => _targetObservation;

	/// <summary>Gets the most recent target-incarnation validation made for a session operation, if any.</summary>
	public TargetIdentityCheck? LastTargetCheck
	{
		get;
		private set;
	}

	/// <summary>Gets why the session was conservatively invalidated, or <see cref="MemoryScanInvalidationReason.None" />.</summary>
	public MemoryScanInvalidationReason InvalidationReason
	{
		get;
		private set;
	}

	/// <summary>Gets how cancellation intersected the most recent cancellable scan or materialization operation.</summary>
	public MemoryScanCancellationMilestone LastCancellationMilestone
	{
		get;
		private set;
	}

	/// <summary>
	///     Gets the stable outcome of the one child-before-parent release attempt, or an unspecified outcome before the
	///     session has been released or abandoned.
	/// </summary>
	public MemoryScanReleaseOutcome LastReleaseOutcome
	{
		get;
		private set;
	}

	/// <summary>
	///     Gets the scanner as a borrowed handle. Direct raw operations on this value bypass the session's state checks;
	///     prefer the session members for the scan lifecycle.
	/// </summary>
	/// <exception cref="ObjectDisposedException">The session was disposed.</exception>
	/// <exception cref="MemoryScanStateException">Another member of the session is inside a CE call.</exception>
	[RequiresPluginEnabled]
	public MemScan Scanner
	{
		get
		{
			ThrowIfDisposed();
			RequireEnabledMainThread();
			BeginSessionCall("Scanner");
			try
			{
				using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
				EnsureCurrentContext(operation.State, "MemoryScan.Scanner");
				return _scanner!.Value;
			}
			finally
			{
				EndSessionCall();
			}
		}
	}

	/// <summary>
	///     Gets the attached found list as a borrowed handle, only after it is initialized for reading. Direct raw
	///     operations on the returned value bypass the session's state checks.
	/// </summary>
	/// <exception cref="MemoryScanStateException">
	///     The results are not ready, or another member of the session is inside a CE call.
	/// </exception>
	/// <exception cref="ObjectDisposedException">The session was disposed.</exception>
	[RequiresPluginEnabled]
	public FoundList Results
	{
		get
		{
			RequireState("Results", MemoryScanState.ResultsReady);
			RequireEnabledMainThread();
			BeginSessionCall("Results");
			try
			{
				using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
				EnsureCurrentContext(operation.State, "MemoryScan.Results");
				return _foundList!.Value;
			}
			finally
			{
				EndSessionCall();
			}
		}
	}

	/// <summary>Gets the number of readable results in the initialized found list.</summary>
	/// <remarks>
	///     Cheat Engine stores the count as <c>UInt64</c>. The Lua boundary supplies a signed 64-bit integer, so this
	///     property exposes its non-negative range as <see cref="ulong" /> and rejects an unrepresentable host result.
	///     Result-reading methods intentionally retain CE's <see cref="int" /> index parameter and therefore address
	///     only indices from zero through <c>Int32.MaxValue</c>.
	/// </remarks>
	/// <exception cref="MemoryScanStateException">
	///     The results are not ready, or another member of the session is inside a CE call.
	/// </exception>
	/// <exception cref="MemoryScanException">CE did not return a valid non-negative integer count.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public ulong ResultCount
	{
		get
		{
			RequireEnabledMainThread();
			BeginSessionCall("ResultCount");
			try
			{
				using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
				EnsureCurrentContext(operation.State, ResultCountOperation);
				return ReadResultCount(operation.State, RequireResults());
			}
			finally
			{
				EndSessionCall();
			}
		}
	}

	// Whether a release or abandon was requested from inside the active member's CE calls: that member then makes no
	// further CE call.
	private bool IsDisposalRequested => _deferredDisposal != DeferredDisposal.None;

	/// <summary>Best-effort, no-throw disposal that consumes both owners and never implicitly retries CE cleanup.</summary>
	/// <remarks>
	///     Use <see cref="ReleaseWithOutcome" /> when the factual child and parent outcomes matter. This method is
	///     idempotent; it performs neither a target-dependent destroy on a worker nor a retry after an uncertain native
	///     destroy. It records safe refusal or an unconfirmed result in <see cref="LastReleaseOutcome" /> instead. When a
	///     scan may still be running it first requests one cooperative stop and waits for it (at most five seconds), so it
	///     can block CE's main thread; see <see cref="ReleaseWithOutcome" />.
	/// </remarks>
	[MainThreadOnly]
	public void Dispose()
	{
		_ = ReleaseWithOutcome();
	}

	/// <summary>
	///     Consumes the found-list child and scanner parent in that order and returns both one-shot cleanup outcomes.
	/// </summary>
	/// <returns>
	///     A stable result that identifies consumed ownership independently from confirmed, refused, unavailable, or
	///     unconfirmed cleanup. The same result is returned after the session is already disposed.
	/// </returns>
	/// <remarks>
	///     <para>
	///         This method never throws and never retries a destroy. If cleanup cannot safely begin (for example, a worker
	///         thread, detached runtime, changed Lua identity, or changed target), it consumes the managed owners through
	///         <see cref="Owned{T}.Abandon" /> and reports the refusal or unavailable cleanup rather than routing handles
	///         into a different CE context; <see cref="MemoryScanReleaseOutcome.Termination" /> is then
	///         <see cref="MemoryScanTerminationStatus.NotInvoked" /> when a scan may still run and no stop was requested
	///         before, or the unconfirmed status of the stop that was. A protected destroy failure consumes the
	///         corresponding owner and remains unconfirmed.
	///     </para>
	///     <para>
	///         When a scan may still be running (after a first or next scan call, including one that failed after CE may
	///         have started, or after a wait that failed or timed out) and no stop was requested yet, the release calls
	///         <c>terminateScan(false)</c> and then <c>waitTillDone(5000)</c>, each once, before the child and parent
	///         destroys. A stop that is not confirmed is reported in <see cref="MemoryScanReleaseOutcome.Termination" />
	///         and the child and parent are still destroyed once each; a stop already requested through
	///         <see cref="TryTerminateScan" /> is never repeated. This can block CE's main thread for up to five seconds plus
	///         CE's own destroy wait.
	///     </para>
	///     <para>
	///         When it is called from inside a CE call that another member of this session is making (CE's waits can run
	///         queued main-thread work, see the type remarks), it makes no CE call and returns the unspecified default
	///         outcome (both statuses <see cref="TargetReleaseStatus.Unspecified" />, ownership not consumed,
	///         <see cref="MemoryScanTerminationStatus.Unknown" />): the single release is deferred until that CE call has
	///         returned and then runs once, and <see cref="LastReleaseOutcome" /> holds its final outcome. A call made
	///         while the release itself is in progress returns the same provisional outcome and never starts a second one.
	///     </para>
	/// </remarks>
	public MemoryScanReleaseOutcome ReleaseWithOutcome()
	{
		if (State == MemoryScanState.Disposed)
		{
			return LastReleaseOutcome;
		}

		if (LuaRuntime.IsAttached && !LuaRuntime.IsMainThread)
		{
			return ConsumeWithoutCleanup(TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure));
		}

		if (_activeOperation is not null)
		{
			// Re-entered from inside one of this session's own CE calls: never destroy under that call. The active
			// member runs the one release when its CE call returns (the release itself simply completes).
			if (_deferredDisposal == DeferredDisposal.None)
			{
				_deferredDisposal = DeferredDisposal.Release;
			}

			return default;
		}

		return ReleaseAsActiveCall();
	}

	/// <summary>
	///     Stops managed cleanup without invoking CE and makes this session unusable.
	/// </summary>
	/// <remarks>
	///     This is an explicit recovery path for a session whose original Lua runtime or target cannot be validated.
	///     It deliberately does not claim that native destruction occurred, and it must not be used as normal cleanup.
	///     The found-list owner is abandoned before the scanner owner to preserve the parent/child ownership direction.
	///     When it is called from inside a CE call that another member of this session is making, the abandon is
	///     deferred until that CE call has returned (it takes precedence over a release requested the same way); while
	///     the release itself is in progress, the release completes and this call has no effect.
	/// </remarks>
	public void Abandon()
	{
		if (State == MemoryScanState.Disposed)
		{
			return;
		}

		if (_activeOperation is not null)
		{
			if (!_releaseInProgress)
			{
				_deferredDisposal = DeferredDisposal.Abandon;
			}

			return;
		}

		_ = ConsumeWithoutCleanup(TargetReleaseOutcome.NotInvoked());
	}

	/// <summary>
	///     Transfers two explicit ownership wrappers into a session. The source wrappers become empty; the returned
	///     session is then their only intended destroy owner.
	/// </summary>
	/// <param name="scanner">An owned scanner whose ownership has been proven by the caller's binding.</param>
	/// <param name="foundList">An owned result-list child attached to <paramref name="scanner" />.</param>
	/// <returns>A new session in <see cref="MemoryScanState.New" />.</returns>
	/// <exception cref="ArgumentNullException">Either ownership wrapper is <see langword="null" />.</exception>
	/// <exception cref="ObjectDisposedException">Either ownership wrapper was already released or disposed.</exception>
	/// <remarks>
	///     <see cref="MemoryScanSessions.TryCreate" /> is preferred for ordinary CE 7.7 code because it owns the
	///     concrete factory sequence and rolls back a created parent when child creation fails. This method is for an
	///     SDK-sourced binding that already carries the same ownership proof; the <see cref="Owned{T}" /> constructor is
	///     internal, so normal consumers cannot turn an arbitrary borrowed handle into one of these owners.
	/// </remarks>
	public static MemoryScanSession Adopt(Owned<MemScan> scanner, Owned<FoundList> foundList)
	{
		RequireEnabledMainThread();
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		MemoryScanSessionContext context = MemoryScanSessionContext.Capture(operation.State);
		if (!context.TargetObservation.IsQualified)
		{
			throw new MemoryScanException(MemoryScanFailureKind.TargetIdentityUnavailable, "MemoryScan.Adopt",
				"The memory scan session cannot adopt target-dependent owners without a qualified target incarnation.");
		}

		MemoryScanSession session = AdoptUnbound(scanner, foundList);
		session.Bind(context);
		return session;
	}

	internal static MemoryScanSession AdoptUnbound(Owned<MemScan> scanner, Owned<FoundList> foundList)
	{
		return AdoptCore(scanner, foundList, CreateAdoptedSession);
	}

	// Preparing both destinations first means an allocation failure during session construction leaves both source
	// wrappers intact for the factory's child-before-parent rollback.
	internal static MemoryScanSession AdoptCore(Owned<MemScan> scanner, Owned<FoundList> foundList,
		MemoryScanSessionAdopter adopter)
	{
		ArgumentNullException.ThrowIfNull(scanner);
		ArgumentNullException.ThrowIfNull(foundList);
		ArgumentNullException.ThrowIfNull(adopter);

		// Value validates each source wrapper before preparation. The wrappers are deliberately single-owner and
		// unsynchronized, exactly like Owned<T>; callers must not concurrently dispose them.
		_ = scanner.Value;
		_ = foundList.Value;
		Owned<MemScan> adoptedScanner = scanner.PrepareTransfer();
		Owned<FoundList> adoptedFoundList = foundList.PrepareTransfer();
		MemoryScanSession session = adopter(adoptedScanner, adoptedFoundList);
		scanner.CompleteTransfer(adoptedScanner);
		foundList.CompleteTransfer(adoptedFoundList);
		return session;
	}

	private static MemoryScanSession CreateAdoptedSession(Owned<MemScan> scanner, Owned<FoundList> foundList)
	{
		return new MemoryScanSession(scanner, foundList);
	}

	internal void Bind(MemoryScanSessionContext context)
	{
		if (_isBound)
		{
			throw new InvalidOperationException("A memory scan session cannot be bound to two runtime contexts.");
		}

		RuntimeIdentity = context.RuntimeIdentity;
		_targetObservation = context.TargetObservation;
		_isBound = true;
	}

	/// <summary>Begins a CE first scan with all fourteen documented positional arguments.</summary>
	/// <param name="request">The complete first-scan request.</param>
	/// <exception cref="MemoryScanStateException">The session is not new.</exception>
	/// <exception cref="ArgumentException">The request contains an unsupported first-scan option or value type.</exception>
	/// <exception cref="ArgumentNullException">A required CE string argument is <see langword="null" />.</exception>
	/// <exception cref="MemoryScanException">The protected CE method call failed.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void StartFirstScan(in FirstScanRequest request)
	{
		StartFirstScanCancellable(in request, CancellationToken.None);
	}

	/// <summary>Begins a CE first scan and observes cancellation only before or after the synchronous CE call.</summary>
	/// <param name="request">The complete first-scan request.</param>
	/// <param name="cancellationToken">A cooperative cancellation observation token; it cannot interrupt CE.</param>
	/// <exception cref="OperationCanceledException">Cancellation was observed before the CE <c>firstScan</c> call began.</exception>
	/// <exception cref="ObjectDisposedException">The session was released from inside this call (see the type remarks).</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void StartFirstScanCancellable(in FirstScanRequest request, CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		RequireState("StartFirstScan", MemoryScanState.New);
		ValidateFirstRequest(in request);
		BeginSessionCall("StartFirstScan");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, FirstScanOperation);
			ThrowIfCancelledBeforeNativeCall(cancellationToken);

			// A CE error may happen after it accepts some scan setup. Do not report the old New state after a partial
			// call.
			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			_scanMayBeRunning = true;
			CallFirstScan(operation.State, _scanner!.Value, in request);
			CompleteTransition(MemoryScanState.Scanning);
			ObserveCancellationAfterNativeCall(cancellationToken);
		}
		finally
		{
			EndSessionCall();
		}

		ThrowIfReleasedDuringCall("StartFirstScan");
	}

	/// <summary>Begins a CE next scan over the previous readable result set.</summary>
	/// <param name="request">The complete next-scan request.</param>
	/// <exception cref="MemoryScanStateException">The session has not completed a first or previous next scan.</exception>
	/// <exception cref="ArgumentException">The request contains an unsupported next-scan option.</exception>
	/// <exception cref="ArgumentNullException">A required CE string argument is <see langword="null" />.</exception>
	/// <exception cref="MemoryScanException">The protected CE method call failed.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void StartNextScan(in NextScanRequest request)
	{
		StartNextScanCancellable(in request, CancellationToken.None);
	}

	/// <summary>Begins a CE next scan and observes cancellation only before or after the synchronous CE call.</summary>
	/// <param name="request">The complete next-scan request.</param>
	/// <param name="cancellationToken">A cooperative cancellation observation token; it cannot interrupt CE.</param>
	/// <exception cref="OperationCanceledException">Cancellation was observed before the CE <c>nextScan</c> call began.</exception>
	/// <exception cref="ObjectDisposedException">The session was released from inside this call (see the type remarks).</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void StartNextScanCancellable(in NextScanRequest request, CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		RequireState("StartNextScan", MemoryScanState.ResultsReady);
		ValidateNextRequest(in request);
		BeginSessionCall("StartNextScan");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, NextScanOperation);
			ThrowIfCancelledBeforeNativeCall(cancellationToken);

			// The list stops being readable as soon as this session releases it. A failed deinitialize or nextScan
			// leaves the conservative Invalidated state, from which Reset is the only recovery.
			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			CallNoResult(operation.State, _foundList!.Value.Handle, "deinitialize"u8, DeinitializeResultsOperation);
			_scanMayBeRunning = true;
			CallNextScan(operation.State, _scanner!.Value, in request);
			CompleteTransition(MemoryScanState.Scanning);
			ObserveCancellationAfterNativeCall(cancellationToken);
		}
		finally
		{
			EndSessionCall();
		}

		ThrowIfReleasedDuringCall("StartNextScan");
	}

	/// <summary>
	///     Waits through CE's no-timeout <c>waitTillDone()</c> form, then initializes the attached found list only after
	///     CE reports completion.
	/// </summary>
	/// <remarks>
	///     This is CE's documented blocking form: it has no deadline and no cancellation argument. CE 7.7.0.10621 also
	///     has a timeout form; <see cref="TryWaitForCompletion" /> projects it experimentally.
	/// </remarks>
	/// <exception cref="MemoryScanStateException">The session is not scanning.</exception>
	/// <exception cref="MemoryScanException">The protected CE call failed.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void WaitForCompletion()
	{
		WaitForCompletionCancellable(CancellationToken.None);
	}

	/// <summary>
	///     Waits through CE's synchronous no-timeout <c>waitTillDone()</c> form and records whether cancellation was
	///     observed before or after that native call.
	/// </summary>
	/// <param name="cancellationToken">A cooperative cancellation observation token; it cannot interrupt CE.</param>
	/// <exception cref="OperationCanceledException">Cancellation was observed before CE <c>waitTillDone()</c> began.</exception>
	/// <exception cref="ObjectDisposedException">
	///     The session was released from inside the wait (CE's wait can run queued main-thread work, see the type
	///     remarks); the found list was not initialized and the release ran once after the wait returned.
	/// </exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void WaitForCompletionCancellable(CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		RequireState("WaitForCompletion", MemoryScanState.Scanning);
		BeginSessionCall("WaitForCompletion");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, WaitForCompletionOperation);
			ThrowIfCancelledBeforeNativeCall(cancellationToken);
			WaitAndInitialize(operation.State, cancellationToken);
		}
		finally
		{
			EndSessionCall();
		}

		ThrowIfReleasedDuringCall("WaitForCompletion");
	}

	private void WaitAndInitialize(LuaState state, CancellationToken cancellationToken)
	{
		try
		{
			CallWaitTillDone(state, _scanner!.Value);
			_scanMayBeRunning = false;
			ObserveCancellationAfterNativeCall(cancellationToken);

			// A release requested from inside the wait runs as soon as this member returns; it needs no readable view.
			if (IsDisposalRequested)
			{
				return;
			}

			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			CallNoResult(state, _foundList!.Value.Handle, "initialize"u8, InitializeResultsOperation);
			CompleteTransition(MemoryScanState.ResultsReady);
		}
		catch
		{
			// A wait error may mean CE is still scanning, completed, or left a partially materialized result set.
			// Never leave the session in Scanning when it cannot safely decide which of those is true.
			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			throw;
		}
	}

	/// <summary>
	///     Releases any initialized readable view and asks CE to clear the current scan results through
	///     <c>MemScan.newScan</c>.
	/// </summary>
	/// <exception cref="MemoryScanStateException">The session is scanning or disposed.</exception>
	/// <exception cref="MemoryScanException">The protected CE method call failed.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void Reset()
	{
		ResetCancellable(CancellationToken.None);
	}

	/// <summary>Resets the CE scan only when cancellation was not observed before the first native cleanup call.</summary>
	/// <param name="cancellationToken">A cooperative cancellation observation token; it cannot interrupt CE.</param>
	/// <exception cref="OperationCanceledException">Cancellation was observed before CE cleanup began.</exception>
	/// <exception cref="MemoryScanStateException">
	///     The session is scanning or disposed, or a cooperative stop requested through
	///     <see cref="TryTerminateScan" /> was not confirmed (release or abandon the session instead), or another member
	///     of the session is inside a CE call.
	/// </exception>
	/// <exception cref="ObjectDisposedException">The session was released from inside this call (see the type remarks).</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void ResetCancellable(CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		ThrowIfDisposed();
		if (State == MemoryScanState.New)
		{
			return;
		}

		if (State == MemoryScanState.Scanning ||
			(_terminationAttempted && _termination != MemoryScanTerminationStatus.Confirmed))
		{
			ThrowWrongState("Reset");
		}

		BeginSessionCall("Reset");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, ResetOperation);
			ThrowIfCancelledBeforeNativeCall(cancellationToken);

			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			CallNoResult(operation.State, _foundList!.Value.Handle, "deinitialize"u8, DeinitializeResultsOperation);
			CallNoResult(operation.State, _scanner!.Value.Handle, "newScan"u8, ResetOperation);

			// CE's newScan stops a running scan controller before clearing its results (ObservedSource: cheat-engine/
			// cheat-engine@ec45d5f memscan.pas:8360-8372), so a successful reset ends the previous scan's lifecycle.
			_scanMayBeRunning = false;
			_terminationAttempted = false;
			_termination = MemoryScanTerminationStatus.Unknown;
			CompleteTransition(MemoryScanState.New);
			ObserveCancellationAfterNativeCall(cancellationToken);
		}
		finally
		{
			EndSessionCall();
		}

		ThrowIfReleasedDuringCall("Reset");
	}

	/// <summary>
	///     Waits for the running scan through CE's <c>waitTillDone(timeout)</c> form for at most
	///     <paramref name="timeout" /> (the call deadline), then initializes the attached found list only after CE
	///     reports completion.
	/// </summary>
	/// <param name="timeout">
	///     The call deadline: strictly positive and at most <see cref="int.MaxValue" /> milliseconds. A sub-millisecond
	///     value rounds up to one millisecond. <see cref="Timeout.InfiniteTimeSpan" /> is refused: use
	///     <see cref="WaitForCompletion" /> for CE's no-timeout form.
	/// </param>
	/// <returns>
	///     <see cref="MemoryScanWaitStatus.Completed" /> when results are ready; <see cref="MemoryScanWaitStatus.TimedOut" />
	///     when the deadline expired first (the session stays scanning and the scan may still run: wait again, call
	///     <see cref="TryTerminateScan" />, or release the session); a context status without any CE call; otherwise a
	///     failure that invalidates the session. Categories come from the result's Lua type, never from error text.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout" /> is outside the accepted range.</exception>
	/// <exception cref="MemoryScanStateException">
	///     The session is not scanning, or another member of the session is inside a CE call.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	///     The session was disposed, including by a release requested from inside this wait (see the type remarks): the
	///     found list was then not initialized and the release ran once after the wait returned.
	/// </exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	/// <remarks>
	///     The call pushes exactly one integer argument and reads exactly one boolean result. The <see langword="false" />
	///     (timed-out) path was not observed on the pinned CE 7.7.0.10621 host (spike D4.7), which is why this member is
	///     experimental. It blocks CE's main thread for at most the deadline.
	/// </remarks>
	[Experimental("CESDK5010",
		UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md")]
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public MemoryScanWaitStatus TryWaitForCompletion(TimeSpan timeout)
	{
		int milliseconds = ToWaitMilliseconds(timeout, nameof(timeout));
		RequireEnabledMainThread();
		RequireState("TryWaitForCompletion", MemoryScanState.Scanning);
		MemoryScanWaitStatus status;
		BeginSessionCall("TryWaitForCompletion");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			MemoryScanMaterializationStatus context = TryEnsureCurrentContext(operation.State);
			status = context == MemoryScanMaterializationStatus.Success
				? WaitCore(operation.State, milliseconds, true, out _)
				: ToWaitStatus(context);
		}
		finally
		{
			EndSessionCall();
		}

		ThrowIfReleasedDuringCall("TryWaitForCompletion");
		return status;
	}

	/// <summary>
	///     Requests a cooperative stop of a scan that may still be running (<c>terminateScan(false)</c>), then waits for
	///     it through <c>waitTillDone(timeout)</c> for at most <paramref name="waitTimeout" />.
	/// </summary>
	/// <param name="waitTimeout">
	///     The deadline of the settle wait: strictly positive and at most <see cref="int.MaxValue" /> milliseconds; a
	///     sub-millisecond value rounds up to one millisecond.
	/// </param>
	/// <returns>
	///     <see cref="MemoryScanTerminationStatus.Confirmed" /> when CE confirmed the stop;
	///     <see cref="MemoryScanTerminationStatus.NotInvoked" /> when the session's runtime or target context was refused
	///     (no CE call); otherwise an unconfirmed stop. The request is made once and never retried.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="waitTimeout" /> is outside the accepted range.</exception>
	/// <exception cref="MemoryScanStateException">
	///     No scan can be running (the session never started one, or a wait, a confirmed stop or a reset already ended
	///     it), a stop was already requested for this scan, or another member of the session is inside a CE call.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	///     The session was disposed, including by a release requested from inside this call's settle wait (see the type
	///     remarks); that release ran once after the wait returned, without a second stop request, and its
	///     <see cref="MemoryScanReleaseOutcome.Termination" /> reports an unconfirmed stop.
	/// </exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	/// <remarks>
	///     <para>
	///         The stop is always cooperative: the SDK passes an explicit <see langword="false" /> force argument and exposes
	///         no force option (a forced stop can kill CE's scan thread and open a modal dialog). The session always ends
	///         <see cref="MemoryScanState.Invalidated" />, with <see cref="MemoryScanInvalidationReason.ScanTerminated" />
	///         unless a context check recorded a more specific reason, and never exposes the stopped scan's results.
	///     </para>
	///     <para>
	///         After <see cref="MemoryScanTerminationStatus.Confirmed" />, <see cref="Reset" /> can start a new lifecycle.
	///         After any other status, <see cref="Reset" /> is refused; <see cref="ReleaseWithOutcome" /> and
	///         <see cref="Abandon" /> stay available, and release reports this unconfirmed status without a second stop
	///         request. <c>terminateScan</c> and the timed-out wait were not observed on the pinned CE 7.7.0.10621 host
	///         (spike D4.7), which is why this member is experimental.
	///     </para>
	/// </remarks>
	[Experimental("CESDK5010",
		UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md")]
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public MemoryScanTerminationStatus TryTerminateScan(TimeSpan waitTimeout)
	{
		int milliseconds = ToWaitMilliseconds(waitTimeout, nameof(waitTimeout));
		RequireEnabledMainThread();
		ThrowIfDisposed();
		if (!_scanMayBeRunning || _terminationAttempted)
		{
			ThrowWrongState("TryTerminateScan");
		}

		MemoryScanTerminationStatus status;
		BeginSessionCall("TryTerminateScan");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			MemoryScanMaterializationStatus context = TryEnsureCurrentContext(operation.State);
			if (context == MemoryScanMaterializationStatus.Success)
			{
				status = TerminateAndSettleCore(operation.State, milliseconds);
				Invalidate(MemoryScanInvalidationReason.ScanTerminated);
			}
			else
			{
				if (State != MemoryScanState.Invalidated)
				{
					Invalidate(MemoryScanInvalidationReason.ScanTerminated);
				}

				status = MemoryScanTerminationStatus.NotInvoked;
			}
		}
		finally
		{
			EndSessionCall();
		}

		ThrowIfReleasedDuringCall("TryTerminateScan");
		return status;
	}

	/// <summary>
	///     Copies CE's <c>MemScan.ErrorString</c> text as a bounded fact, without interpreting it.
	/// </summary>
	/// <param name="text">
	///     The copied text when the method returns <see langword="true" /> (possibly empty): at most
	///     <see cref="HostErrorTextMaximumUtf8Bytes" /> UTF-8 bytes, cut back to a UTF-8 sequence boundary, with invalid
	///     sequences decoded as U+FFFD and embedded NUL characters kept.
	/// </param>
	/// <param name="truncated">Whether the host text was longer than the copied prefix.</param>
	/// <returns>
	///     <see langword="false" /> without any scanner call when the session's runtime or target context is refused; as
	///     for every session operation, a changed runtime or target incarnation then invalidates the session
	///     (<see cref="MemoryScanInvalidationReason.RuntimeIdentityChanged" />,
	///     <see cref="MemoryScanInvalidationReason.TargetChanged" /> or
	///     <see cref="MemoryScanInvalidationReason.TargetProcessReused" />). <see langword="false" /> when the property
	///     read raised or did not return a Lua string, which leaves the session state unchanged.
	/// </returns>
	/// <exception cref="ObjectDisposedException">The session was disposed.</exception>
	/// <exception cref="MemoryScanStateException">Another member of the session is inside a CE call.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	/// <remarks>
	///     Available in every state but disposed. CE's error text is a host-language diagnostic: its presence can be
	///     reported, but the SDK never derives an outcome category from its content (it changes with CE's UI language,
	///     and CE reports a misleading text for an empty range, spike D4.3).
	/// </remarks>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public bool TryGetHostErrorText([NotNullWhen(true)] out string? text, out bool truncated)
	{
		ThrowIfDisposed();
		RequireEnabledMainThread();
		BeginSessionCall("TryGetHostErrorText");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			if (TryEnsureCurrentContext(operation.State) != MemoryScanMaterializationStatus.Success)
			{
				text = null;
				truncated = false;
				return false;
			}

			return TryReadHostErrorTextCore(operation.State, out text, out truncated);
		}
		finally
		{
			EndSessionCall();
		}
	}

	/// <summary>Attempts to read the parsed target address at a zero-based result index.</summary>
	/// <param name="zeroBasedIndex">
	///     The CE found-list index, beginning at zero. This API deliberately supports only the managed
	///     <see cref="int" /> index range even when <see cref="ResultCount" /> is larger.
	/// </param>
	/// <param name="address">The parsed target address when the method returns <see langword="true" />.</param>
	/// <returns><see langword="false" /> when the index is outside the current result count.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	/// <exception cref="MemoryScanStateException">The results are not ready.</exception>
	/// <exception cref="MemoryScanException">CE failed or returned an address string that cannot be parsed.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public bool TryGetAddress(int zeroBasedIndex, out Address address)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
		RequireEnabledMainThread();
		BeginSessionCall("TryGetAddress");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, ResultAddressOperation);
			FoundList foundList = RequireResults();
			if ((ulong) zeroBasedIndex >= ReadResultCount(operation.State, foundList))
			{
				address = default;
				return false;
			}

			string text = CallString(operation.State, foundList.Handle, "getAddress"u8, ResultAddressOperation,
				zeroBasedIndex);
			if (Address.TryParse(text, out address))
			{
				return true;
			}

			throw new MemoryScanException(MemoryScanFailureKind.UnexpectedResult, ResultAddressOperation,
				"The memory scan result address was not a hexadecimal target address.");
		}
		finally
		{
			EndSessionCall();
		}
	}

	/// <summary>Attempts to read the exact value text at a zero-based result index.</summary>
	/// <param name="zeroBasedIndex">
	///     The CE found-list index, beginning at zero. This API deliberately supports only the managed
	///     <see cref="int" /> index range even when <see cref="ResultCount" /> is larger.
	/// </param>
	/// <param name="value">The copied CE value text when the method returns <see langword="true" />.</param>
	/// <returns><see langword="false" /> when the index is outside the current result count.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	/// <exception cref="MemoryScanStateException">The results are not ready.</exception>
	/// <exception cref="MemoryScanException">The protected CE method call failed or did not return text.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public bool TryGetValue(int zeroBasedIndex, [NotNullWhen(true)] out string? value)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
		RequireEnabledMainThread();
		BeginSessionCall("TryGetValue");
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, ResultValueOperation);
			FoundList foundList = RequireResults();
			if ((ulong) zeroBasedIndex >= ReadResultCount(operation.State, foundList))
			{
				value = null;
				return false;
			}

			value = CallString(operation.State, foundList.Handle, "getValue"u8, ResultValueOperation, zeroBasedIndex);
			return true;
		}
		finally
		{
			EndSessionCall();
		}
	}

	/// <summary>Copies the complete initialized found list into a caller-bounded managed destination.</summary>
	/// <param name="destination">The caller-owned storage for copied address/value rows.</param>
	/// <param name="totalCount">The complete CE row count when it was read successfully; otherwise zero.</param>
	/// <param name="written">
	///     The copied row count, zero unless the returned status is
	///     <see cref="MemoryScanMaterializationStatus.Success" />.
	/// </param>
	/// <returns>A success, empty-result, capacity, cancellation, context, Lua, or malformed-result category.</returns>
	/// <remarks>
	///     This method never returns a CE handle, enumerable, or deferred producer. It reads the count once, refuses a
	///     destination that cannot hold every row without issuing row calls, builds a temporary complete snapshot, then
	///     copies it into <paramref name="destination" /> only on success. It is a low-level bounded copy primitive;
	///     workflow-level cardinality and progress policy remain outside the SDK session.
	/// </remarks>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public MemoryScanMaterializationStatus TryCopyResults(Span<MemoryScanResult> destination, out ulong totalCount,
		out int written)
	{
		return TryCopyResultsCancellable(destination, out totalCount, out written, CancellationToken.None);
	}

	/// <summary>Copies the complete initialized found list while observing cancellation between synchronous CE row calls.</summary>
	/// <param name="destination">The caller-owned storage for copied address/value rows.</param>
	/// <param name="totalCount">The complete CE row count when it was read successfully; otherwise zero.</param>
	/// <param name="written">
	///     The copied row count, zero unless the returned status is
	///     <see cref="MemoryScanMaterializationStatus.Success" />.
	/// </param>
	/// <param name="cancellationToken">A cooperative cancellation observation token; it is checked between copied rows.</param>
	/// <returns>A success, empty-result, capacity, cancellation, context, Lua, or malformed-result category.</returns>
	/// <remarks>
	///     Cancellation prevents later row calls or publication of the temporary snapshot. It cannot interrupt a row
	///     call that Cheat Engine has already begun.
	/// </remarks>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public MemoryScanMaterializationStatus TryCopyResultsCancellable(Span<MemoryScanResult> destination,
		out ulong totalCount, out int written, CancellationToken cancellationToken)
	{
		totalCount = 0;
		written = 0;
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		if (State != MemoryScanState.ResultsReady)
		{
			ThrowWrongState("TryCopyResults");
		}

		BeginSessionCall("TryCopyResults");
		try
		{
			return CopyAllResults(destination, out totalCount, out written, cancellationToken);
		}
		finally
		{
			EndSessionCall();
		}
	}

	private MemoryScanMaterializationStatus CopyAllResults(Span<MemoryScanResult> destination, out ulong totalCount,
		out int written, CancellationToken cancellationToken)
	{
		totalCount = 0;
		written = 0;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		MemoryScanMaterializationStatus context = TryEnsureCurrentContext(operation.State);
		if (context != MemoryScanMaterializationStatus.Success)
		{
			return context;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			LastCancellationMilestone = MemoryScanCancellationMilestone.CancelledBeforeNativeCall;
			return MemoryScanMaterializationStatus.Cancelled;
		}

		FoundList foundList = RequireResults();
		try
		{
			totalCount = ReadResultCount(operation.State, foundList);
			if (totalCount == 0)
			{
				return MemoryScanMaterializationStatus.NoResults;
			}

			if (totalCount > int.MaxValue || totalCount > (ulong) destination.Length)
			{
				return MemoryScanMaterializationStatus.DestinationTooSmall;
			}

			MemoryScanResult[] snapshot = new MemoryScanResult[(int) totalCount];
			MemoryScanMaterializationStatus status = TryFillSnapshot(operation.State, foundList, snapshot, 0,
				cancellationToken);
			if (status != MemoryScanMaterializationStatus.Success)
			{
				return status;
			}

			snapshot.AsSpan().CopyTo(destination);
			written = snapshot.Length;
			return MemoryScanMaterializationStatus.Success;
		}
		catch (MemoryScanException exception)
		{
			return ToMaterializationStatus(exception);
		}
	}

	/// <summary>
	///     Copies one caller-bounded page of initialized results without allocating storage for the complete found list.
	/// </summary>
	/// <param name="firstResultIndex">The zero-based index of the first row requested for this page.</param>
	/// <param name="destination">The caller-owned maximum page storage.</param>
	/// <param name="totalCount">The complete CE row count when it was read successfully; otherwise zero.</param>
	/// <param name="written">The page row count, zero unless the returned status is successful.</param>
	/// <returns>A success, empty-result, page-boundary, capacity, cancellation, context, Lua, or malformed-result category.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="firstResultIndex" /> is negative.</exception>
	/// <remarks>
	///     The temporary staging array is limited to this page, never the full count. No page prefix is copied to
	///     <paramref name="destination" /> if cancellation is observed or any page row is malformed.
	/// </remarks>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public MemoryScanMaterializationStatus TryCopyResultsPage(int firstResultIndex, Span<MemoryScanResult> destination,
		out ulong totalCount, out int written)
	{
		return TryCopyResultsPageCancellable(firstResultIndex, destination, out totalCount, out written,
			CancellationToken.None);
	}

	/// <summary>
	///     Copies one caller-bounded result page while observing cancellation between synchronous CE row calls.
	/// </summary>
	/// <param name="firstResultIndex">The zero-based index of the first row requested for this page.</param>
	/// <param name="destination">The caller-owned maximum page storage.</param>
	/// <param name="totalCount">The complete CE row count when it was read successfully; otherwise zero.</param>
	/// <param name="written">The page row count, zero unless the returned status is successful.</param>
	/// <param name="cancellationToken">A cooperative cancellation token; it cannot interrupt a CE row call already begun.</param>
	/// <returns>A success, empty-result, page-boundary, capacity, cancellation, context, Lua, or malformed-result category.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="firstResultIndex" /> is negative.</exception>
	/// <remarks>
	///     This operation stages at most <paramref name="destination" />.Length rows, then publishes the page only when
	///     every staged address/value pair is valid and cancellation has not been observed.
	/// </remarks>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public MemoryScanMaterializationStatus TryCopyResultsPageCancellable(int firstResultIndex,
		Span<MemoryScanResult> destination, out ulong totalCount, out int written, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(firstResultIndex);
		totalCount = 0;
		written = 0;
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		if (State != MemoryScanState.ResultsReady)
		{
			ThrowWrongState("TryCopyResultsPage");
		}

		BeginSessionCall("TryCopyResultsPage");
		try
		{
			return CopyResultsPage(firstResultIndex, destination, out totalCount, out written, cancellationToken);
		}
		finally
		{
			EndSessionCall();
		}
	}

	private MemoryScanMaterializationStatus CopyResultsPage(int firstResultIndex, Span<MemoryScanResult> destination,
		out ulong totalCount, out int written, CancellationToken cancellationToken)
	{
		totalCount = 0;
		written = 0;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		MemoryScanMaterializationStatus context = TryEnsureCurrentContext(operation.State);
		if (context != MemoryScanMaterializationStatus.Success)
		{
			return context;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			LastCancellationMilestone = MemoryScanCancellationMilestone.CancelledBeforeNativeCall;
			return MemoryScanMaterializationStatus.Cancelled;
		}

		FoundList foundList = RequireResults();
		try
		{
			totalCount = ReadResultCount(operation.State, foundList);
			if (totalCount == 0)
			{
				return MemoryScanMaterializationStatus.NoResults;
			}

			if ((ulong) firstResultIndex >= totalCount)
			{
				return MemoryScanMaterializationStatus.PageStartOutOfRange;
			}

			if (destination.IsEmpty)
			{
				return MemoryScanMaterializationStatus.DestinationTooSmall;
			}

			int pageLength = (int) Math.Min((ulong) destination.Length, totalCount - (ulong) firstResultIndex);
			MemoryScanResult[] snapshot = new MemoryScanResult[pageLength];
			MemoryScanMaterializationStatus status = TryFillSnapshot(operation.State, foundList, snapshot,
				firstResultIndex, cancellationToken);
			if (status != MemoryScanMaterializationStatus.Success)
			{
				return status;
			}

			snapshot.AsSpan().CopyTo(destination);
			written = snapshot.Length;
			return MemoryScanMaterializationStatus.Success;
		}
		catch (MemoryScanException exception)
		{
			return ToMaterializationStatus(exception);
		}
	}

	private MemoryScanMaterializationStatus TryFillSnapshot(LuaState state, FoundList foundList,
		MemoryScanResult[] snapshot, int firstResultIndex, CancellationToken cancellationToken)
	{
		for (int offset = 0; offset < snapshot.Length; offset++)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				LastCancellationMilestone = MemoryScanCancellationMilestone.ObservedAfterNativeCall;
				return MemoryScanMaterializationStatus.Cancelled;
			}

			int index = firstResultIndex + offset;
			string addressText = CallString(state, foundList.Handle, "getAddress"u8, ResultAddressOperation, index);
			if (cancellationToken.IsCancellationRequested)
			{
				LastCancellationMilestone = MemoryScanCancellationMilestone.ObservedAfterNativeCall;
				return MemoryScanMaterializationStatus.Cancelled;
			}

			if (!Address.TryParse(addressText, out Address address))
			{
				return MemoryScanMaterializationStatus.InvalidResult;
			}

			string value = CallString(state, foundList.Handle, "getValue"u8, ResultValueOperation, index);
			snapshot[offset] = new MemoryScanResult(address, value);
		}

		if (!cancellationToken.IsCancellationRequested)
		{
			return MemoryScanMaterializationStatus.Success;
		}

		LastCancellationMilestone = MemoryScanCancellationMilestone.ObservedAfterNativeCall;
		return MemoryScanMaterializationStatus.Cancelled;
	}

	private static MemoryScanMaterializationStatus ToMaterializationStatus(MemoryScanException exception)
	{
		return exception.FailureKind == MemoryScanFailureKind.LuaError
			? MemoryScanMaterializationStatus.LuaFailure
			: MemoryScanMaterializationStatus.InvalidResult;
	}

	// Validates a TimeSpan deadline and converts it to CE's integer milliseconds, rounding a partial millisecond up so
	// that a positive deadline never becomes zero.
	internal static int ToWaitMilliseconds(TimeSpan timeout, string parameterName)
	{
		if (timeout <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(parameterName, timeout,
				"A scan wait deadline must be strictly positive; use the no-timeout wait for CE's blocking form.");
		}

		long ticks = timeout.Ticks;
		long milliseconds = (ticks / TimeSpan.TicksPerMillisecond) +
							(ticks % TimeSpan.TicksPerMillisecond == 0 ? 0 : 1);
		if (milliseconds > int.MaxValue)
		{
			throw new ArgumentOutOfRangeException(parameterName, timeout,
				"A scan wait deadline cannot exceed Int32.MaxValue milliseconds, the range of CE's integer timeout.");
		}

		return (int) milliseconds;
	}

	// The shared wait: CE's no-timeout form (0 arguments, 0 results) when timeoutMilliseconds is null, otherwise the
	// timeout form (1 integer argument, 1 boolean result). A completed wait clears the running flag; initializeResults
	// is false only for the first-found route, whose found list must never be initialized.
	internal MemoryScanWaitStatus WaitCore(LuaState state, int? timeoutMilliseconds, bool initializeResults,
		out LuaStatus luaStatus)
	{
		MemoryScanWaitStatus waited = CallWait(state, timeoutMilliseconds, out luaStatus);
		if (waited != MemoryScanWaitStatus.Completed)
		{
			if (waited != MemoryScanWaitStatus.TimedOut)
			{
				Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			}

			return waited;
		}

		_scanMayBeRunning = false;

		// A release requested from inside the wait runs as soon as the active member returns; it needs no readable view.
		if (!initializeResults || IsDisposalRequested)
		{
			return MemoryScanWaitStatus.Completed;
		}

		Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
		using LuaFrame frame = new(state);
		luaStatus = _foundList!.Value.Handle.TryCallMethod(state, "initialize"u8, 0, 0);
		if (!luaStatus.IsOk)
		{
			return MemoryScanWaitStatus.InitializationFailed;
		}

		CompleteTransition(MemoryScanState.ResultsReady);
		return MemoryScanWaitStatus.Completed;
	}

	private MemoryScanWaitStatus CallWait(LuaState state, int? timeoutMilliseconds, out LuaStatus luaStatus)
	{
		using LuaFrame frame = new(state);
		if (timeoutMilliseconds is not { } milliseconds)
		{
			luaStatus = _scanner!.Value.Handle.TryCallMethod(state, "waitTillDone"u8, 0, 0);
			return luaStatus.IsOk ? MemoryScanWaitStatus.Completed : MemoryScanWaitStatus.LuaFailure;
		}

		state.PushInteger(milliseconds);
		luaStatus = _scanner!.Value.Handle.TryCallMethod(state, "waitTillDone"u8, 1, 1);
		if (!luaStatus.IsOk)
		{
			return MemoryScanWaitStatus.LuaFailure;
		}

		if (state.TypeOf(-1) != LuaType.Boolean)
		{
			return MemoryScanWaitStatus.InvalidResult;
		}

		return state.ToBoolean(-1) ? MemoryScanWaitStatus.Completed : MemoryScanWaitStatus.TimedOut;
	}

	// The one cooperative stop request of the current scan: terminateScan(false) (1 argument, 0 results), then
	// waitTillDone(timeout) (1 argument, 1 boolean result). Each is called at most once and never retried; the method
	// never throws because release relies on it.
	internal MemoryScanTerminationStatus TerminateAndSettleCore(LuaState state, int waitMilliseconds)
	{
		_terminationAttempted = true;
		MemoryScanTerminationStatus status = MemoryScanTerminationStatus.TerminateFailed;
		try
		{
			if (RequestTermination(state))
			{
				// CE accepted the stop request: from here on an unexpected failure belongs to the settle wait.
				status = MemoryScanTerminationStatus.WaitFailed;
				status = SettleAfterTermination(state, waitMilliseconds);
			}
		}
		catch (Exception)
		{
			// A binding failure leaves the stop unconfirmed (TerminateFailed before CE accepted the request, WaitFailed
			// after it); it is never retried.
		}

		_termination = status;
		if (status == MemoryScanTerminationStatus.Confirmed)
		{
			_scanMayBeRunning = false;
		}

		return status;
	}

	private bool RequestTermination(LuaState state)
	{
		using LuaFrame frame = new(state);
		state.PushBoolean(false);
		return _scanner!.Value.Handle.TryCallMethod(state, "terminateScan"u8, 1, 0).IsOk;
	}

	private MemoryScanTerminationStatus SettleAfterTermination(LuaState state, int waitMilliseconds)
	{
		using LuaFrame frame = new(state);
		state.PushInteger(waitMilliseconds);
		if (!_scanner!.Value.Handle.TryCallMethod(state, "waitTillDone"u8, 1, 1).IsOk ||
			state.TypeOf(-1) != LuaType.Boolean)
		{
			return MemoryScanTerminationStatus.WaitFailed;
		}

		return state.ToBoolean(-1) ? MemoryScanTerminationStatus.Confirmed : MemoryScanTerminationStatus.WaitTimedOut;
	}

	// Reads the ErrorString property through a protected property read and copies at most the documented bound. Returns
	// false when the read raised or did not produce a Lua string; never classifies the text.
	internal bool TryReadHostErrorTextCore(LuaState state, [NotNullWhen(true)] out string? text, out bool truncated)
	{
		using LuaFrame frame = new(state);
		if (!_scanner!.Value.Handle.TryGetProperty(state, "ErrorString"u8).IsOk ||
			!state.TryReadUtf8(-1, out ReadOnlySpan<byte> utf8))
		{
			text = null;
			truncated = false;
			return false;
		}

		text = DecodeBoundedUtf8(utf8, HostErrorTextMaximumUtf8Bytes, out truncated);
		return true;
	}

	// Decodes at most maximumBytes bytes, cutting back to the start of a UTF-8 sequence so that the copied prefix never
	// ends inside a character. A malformed run of continuation bytes is cut at the bound; invalid sequences become
	// U+FFFD. Embedded NUL bytes are kept.
	internal static string DecodeBoundedUtf8(ReadOnlySpan<byte> utf8, int maximumBytes, out bool truncated)
	{
		if (utf8.Length <= maximumBytes)
		{
			truncated = false;
			return Encoding.UTF8.GetString(utf8);
		}

		int cut = maximumBytes;
		int lowest = Math.Max(0, maximumBytes - 3);
		while (cut > lowest && IsUtf8Continuation(utf8[cut]))
		{
			cut--;
		}

		if (IsUtf8Continuation(utf8[cut]))
		{
			cut = maximumBytes;
		}

		truncated = true;
		return Encoding.UTF8.GetString(utf8[..cut]);
	}

	private static bool IsUtf8Continuation(byte value)
	{
		return (value & 0xC0) == 0x80;
	}

	// Cores for the bounded AOB routes of AobScanner. They run inside the caller's admitted operation and never
	// re-acquire it; state and context rules are enforced by the caller's sequence.

	// MemScan.setOnlyOneResult(value): 1 argument, 0 results.
	internal LuaStatus SetOnlyOneResultCore(LuaState state, bool value)
	{
		using LuaFrame frame = new(state);
		state.PushBoolean(value);
		return _scanner!.Value.Handle.TryCallMethod(state, "setOnlyOneResult"u8, 1, 0);
	}

	// The runtime and target check of every session operation, without throwing.
	internal MemoryScanMaterializationStatus TryEnsureCurrentContextCore(LuaState state)
	{
		return TryEnsureCurrentContext(state);
	}

	// FoundList.getCount() of an initialized list; throws MemoryScanException (LuaError or UnexpectedResult).
	internal ulong ReadResultCountCore(LuaState state)
	{
		return ReadResultCount(state, RequireResults());
	}

	// MemScan.getOnlyResult(): 0 arguments, 1 result. No value or nil is "not found" (celua.txt line 2657); only a Lua
	// integer is an address, read bit for bit so that an address at or above 2^63 keeps its 64-bit pattern. A float is
	// refused rather than converted, like any other non-integer.
	internal MemoryScanOnlyResult TryReadOnlyResultCore(LuaState state, out Address address, out LuaStatus luaStatus)
	{
		using LuaFrame frame = new(state);
		address = default;
		luaStatus = _scanner!.Value.Handle.TryCallMethod(state, "getOnlyResult"u8, 0, 1);
		if (!luaStatus.IsOk)
		{
			return MemoryScanOnlyResult.LuaFailure;
		}

		if (state.IsNil(-1))
		{
			return MemoryScanOnlyResult.NotFound;
		}

		if (!state.IsInteger(-1) || !state.TryReadInteger(-1, out long bits))
		{
			return MemoryScanOnlyResult.InvalidResult;
		}

		address = Address.FromInt64(bits);
		return MemoryScanOnlyResult.Found;
	}

	// FoundList.getAddress(index) of an initialized list, read as UTF-8 and parsed without allocating. It never reads
	// getValue: an address-only copy costs one CE call per row.
	internal MemoryScanRowRead TryReadAddressRowCore(LuaState state, int index, out Address address,
		out LuaStatus luaStatus)
	{
		using LuaFrame frame = new(state);
		state.PushInteger(index);
		luaStatus = RequireResults().Handle.TryCallMethod(state, "getAddress"u8, 1, 1);
		if (!luaStatus.IsOk)
		{
			address = default;
			return MemoryScanRowRead.LuaFailure;
		}

		if (!state.TryReadUtf8(-1, out ReadOnlySpan<byte> utf8) || !Address.TryParse(utf8, out address))
		{
			address = default;
			return MemoryScanRowRead.InvalidResult;
		}

		return MemoryScanRowRead.Read;
	}

	private static MemoryScanWaitStatus ToWaitStatus(MemoryScanMaterializationStatus context)
	{
		return context switch
		{
			MemoryScanMaterializationStatus.RuntimeInvalidated => MemoryScanWaitStatus.RuntimeInvalidated,
			MemoryScanMaterializationStatus.TargetIdentityMismatch => MemoryScanWaitStatus.TargetIdentityMismatch,
			_ => MemoryScanWaitStatus.TargetIdentityUnavailable
		};
	}

	private MemoryScanReleaseOutcome ReleaseWithinCurrentContext(LuaState state)
	{
		Owned<FoundList>? foundList = _foundList;
		Owned<MemScan>? scanner = _scanner;
		MemoryScanTerminationStatus termination = StopRunningScanForRelease(state);
		if (State == MemoryScanState.ResultsReady && foundList is not null && !foundList.IsDisposed)
		{
			using LuaFrame frame = new(state);
			_ = foundList.Value.Handle.TryCallMethod(state, "deinitialize"u8, 0, 0);
		}

		// Destroy the child, then the parent, each exactly once, even after an unconfirmed stop (audit A13-26).
		TargetReleaseOutcome foundListOutcome = ReleaseOwned(state, foundList);
		TargetReleaseOutcome scannerOutcome = ReleaseOwned(state, scanner);
		return CompleteRelease(foundListOutcome, scannerOutcome, termination);
	}

	// The release's one cooperative stop. A stop already requested through TryTerminateScan (or by the bounded AOB
	// route after its deadline) is never repeated: its unconfirmed status is reported instead.
	private MemoryScanTerminationStatus StopRunningScanForRelease(LuaState state)
	{
		if (!_scanMayBeRunning)
		{
			return MemoryScanTerminationStatus.NotRequired;
		}

		return _terminationAttempted
			? _termination
			: TerminateAndSettleCore(state, ReleaseTerminationWaitMilliseconds);
	}

	// Consumes both owners without any CE call. A stop requested earlier (and left unconfirmed) keeps its status; a
	// scan that may run without any stop request reports NotInvoked.
	private MemoryScanReleaseOutcome ConsumeWithoutCleanup(TargetReleaseOutcome outcome)
	{
		MemoryScanTerminationStatus termination = !_scanMayBeRunning
			? MemoryScanTerminationStatus.NotRequired
			: _terminationAttempted
				? _termination
				: MemoryScanTerminationStatus.NotInvoked;
		ConsumeOwner(_foundList);
		ConsumeOwner(_scanner);
		return CompleteRelease(outcome, outcome, termination);
	}

	// The one release: runs as the session's active member so that work CE runs during its waits and destroys cannot
	// start a second release (a re-entrant release returns the provisional default outcome instead).
	private MemoryScanReleaseOutcome ReleaseAsActiveCall()
	{
		_activeOperation = "ReleaseWithOutcome";
		_releaseInProgress = true;
		try
		{
			return ReleaseCore();
		}
		finally
		{
			_releaseInProgress = false;
			_activeOperation = null;
			_deferredDisposal = DeferredDisposal.None;
		}
	}

	private MemoryScanReleaseOutcome ReleaseCore()
	{
		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			MemoryScanMaterializationStatus context = TryEnsureCurrentContext(operation.State);
			if (context != MemoryScanMaterializationStatus.Success)
			{
				return ConsumeWithoutCleanup(CreateRefusedReleaseOutcome(context));
			}

			return ReleaseWithinCurrentContext(operation.State);
		}
		catch (Exception)
		{
			return ConsumeWithoutCleanup(TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure));
		}
	}

	// Marks the start of a member's CE calls. A member called from inside another member's CE call (CE ran queued
	// main-thread work during it) is refused before it makes any CE call.
	private void BeginSessionCall(string operation)
	{
		if (_activeOperation is not null)
		{
			throw new MemoryScanStateException(operation, State, _activeOperation);
		}

		_activeOperation = operation;
	}

	// Marks the end of a member's CE calls, then runs a release or abandon requested from inside them: exactly once,
	// and only now that none of this session's CE calls is on the stack. Never throws.
	private void EndSessionCall()
	{
		_activeOperation = null;
		DeferredDisposal deferred = _deferredDisposal;
		_deferredDisposal = DeferredDisposal.None;
		if (deferred == DeferredDisposal.None || State == MemoryScanState.Disposed)
		{
			return;
		}

		if (deferred == DeferredDisposal.Abandon)
		{
			_ = ConsumeWithoutCleanup(TargetReleaseOutcome.NotInvoked());
			return;
		}

		_ = ReleaseAsActiveCall();
	}

	// After a lifecycle member returned: a release deferred from inside its CE calls has disposed the session, so the
	// member must not report a completed transition.
	private void ThrowIfReleasedDuringCall(string operation)
	{
		if (State == MemoryScanState.Disposed)
		{
			throw new ObjectDisposedException(nameof(MemoryScanSession),
				"The memory-scan session was released or abandoned by a call made while '" + operation +
				"' was inside a Cheat Engine call; that release ran once, after the Cheat Engine call returned.");
		}
	}

	private MemoryScanReleaseOutcome CompleteRelease(TargetReleaseOutcome foundListOutcome,
		TargetReleaseOutcome scannerOutcome, MemoryScanTerminationStatus termination)
	{
		_foundList = null;
		_scanner = null;
		State = MemoryScanState.Disposed;
		LastReleaseOutcome =
			new MemoryScanReleaseOutcome(foundListOutcome, scannerOutcome, true, true, termination);
		return LastReleaseOutcome;
	}

	private TargetReleaseOutcome CreateRefusedReleaseOutcome(MemoryScanMaterializationStatus context)
	{
		if (context is MemoryScanMaterializationStatus.TargetIdentityUnavailable or
				MemoryScanMaterializationStatus.TargetIdentityMismatch && LastTargetCheck.HasValue)
		{
			return TargetReleaseOutcome.Refused(LastTargetCheck.GetValueOrDefault());
		}

		return TargetReleaseOutcome.NotInvoked(context == MemoryScanMaterializationStatus.RuntimeInvalidated
			? EngineFailureKind.BindingFailure
			: EngineFailureKind.TargetIdentityUnavailable);
	}

	private static TargetReleaseOutcome ReleaseOwned<T>(LuaState state, Owned<T>? owner)
		where T : struct, ICEObject<T>
	{
		if (owner is null || owner.IsDisposed)
		{
			return TargetReleaseOutcome.NotInvoked();
		}

		try
		{
			using LuaFrame frame = new(state);
			LuaStatus status = owner.TryDestroy(state);
			return status.IsOk
				? TargetReleaseOutcome.Released()
				: TargetReleaseOutcome.Unconfirmed(EngineFailureKind.ProtectedLuaFailure);
		}
		catch (Exception)
		{
			return TargetReleaseOutcome.Unconfirmed(EngineFailureKind.BindingFailure);
		}
		finally
		{
			ConsumeOwner(owner);
		}
	}

	private static void ConsumeOwner<T>(Owned<T>? owner)
		where T : struct, ICEObject<T>
	{
		if (owner is not null && !owner.IsDisposed)
		{
			try
			{
				_ = owner.Abandon();
			}
			catch (Exception)
			{
				// The session must not make IDisposable cleanup throw or expose a retry path after taking ownership.
			}
		}
	}

	private FoundList RequireResults()
	{
		RequireState("results", MemoryScanState.ResultsReady);
		return _foundList!.Value;
	}

	private void RequireState(string operation, MemoryScanState expected)
	{
		ThrowIfDisposed();
		if (State != expected)
		{
			ThrowWrongState(operation);
		}
	}

	private void ThrowIfDisposed()
	{
		if (State == MemoryScanState.Disposed)
		{
			throw new ObjectDisposedException(nameof(MemoryScanSession), "The memory-scan session was disposed.");
		}
	}

	[DoesNotReturn]
	private void ThrowWrongState(string operation)
	{
		throw new MemoryScanStateException(operation, State);
	}

	private static void RequireEnabledMainThread()
	{
		if (!LuaRuntime.IsAttached)
		{
			throw new InvalidOperationException(
				"The Cheat Engine plugin is not enabled, so the memory scan cannot acquire its Lua state.");
		}

		if (!LuaRuntime.IsMainThread)
		{
			throw new InvalidOperationException(
				"Memory scan operations must run on Cheat Engine's main thread; the session does not dispatch work implicitly.");
		}
	}

	private static void ValidateFirstRequest(in FirstScanRequest request)
	{
		// A default-initialized request can contain null strings.  These are properties of the
		// value-type request rather than parameters of this helper, therefore its parameter name
		// must be the actual public argument ("request") instead of a local alias.
		RequireValue(request.Input1, nameof(request));
		RequireValue(request.Input2, nameof(request));
		RequireValue(request.ProtectionFlags, nameof(request));
		RequireValue(request.AlignmentParameter, nameof(request));

		if (request.ScanOption is < ScanOption.UnknownValue or > ScanOption.SmallerThan)
		{
			throw new ArgumentException(
				"A first scan only accepts UnknownValue, ExactValue, ValueBetween, BiggerThan or SmallerThan.",
				nameof(request));
		}

		// CE 7.7 celua.txt line 2587 lists vtGrouped in addition to the contiguous Byte..All range.
		if ((uint) request.VariableType > (uint) VariableType.All && request.VariableType != VariableType.Grouped)
		{
			throw new ArgumentException("The CE 7.7 firstScan contract accepts Byte through All and Grouped.",
				nameof(request));
		}

		if (request.RoundingType is < RoundingType.Rounded or > RoundingType.Truncated)
		{
			throw new ArgumentException("The rounding type is not a CE 7.7 value.", nameof(request));
		}

		if (request.FastScanMethod is < FastScanMethod.NotAligned or > FastScanMethod.LastDigits)
		{
			throw new ArgumentException("The fast scan method is not a CE 7.7 value.", nameof(request));
		}
	}

	private static void ValidateNextRequest(in NextScanRequest request)
	{
		RequireValue(request.Input1, nameof(request));
		RequireValue(request.Input2, nameof(request));
		if (request.ScanOption is < ScanOption.ExactValue or > ScanOption.Unchanged)
		{
			throw new ArgumentException("A next scan only accepts ExactValue through Unchanged, never UnknownValue.",
				nameof(request));
		}

		if (request.RoundingType is < RoundingType.Rounded or > RoundingType.Truncated)
		{
			throw new ArgumentException("The rounding type is not a CE 7.7 value.", nameof(request));
		}
	}

	private static void RequireValue(string? value, string parameterName)
	{
		if (value is null)
		{
			throw new ArgumentNullException(parameterName);
		}
	}

	private void ThrowIfCancelledBeforeNativeCall(CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		if (!cancellationToken.IsCancellationRequested)
		{
			return;
		}

		LastCancellationMilestone = MemoryScanCancellationMilestone.CancelledBeforeNativeCall;
		throw new OperationCanceledException(cancellationToken);
	}

	private void ObserveCancellationAfterNativeCall(CancellationToken cancellationToken)
	{
		LastCancellationMilestone = cancellationToken.IsCancellationRequested
			? MemoryScanCancellationMilestone.ObservedAfterNativeCall
			: MemoryScanCancellationMilestone.None;
	}

	private void EnsureCurrentContext(LuaState state, string operation)
	{
		MemoryScanMaterializationStatus status = TryEnsureCurrentContext(state);
		if (status == MemoryScanMaterializationStatus.Success)
		{
			return;
		}

		MemoryScanFailureKind failureKind = status switch
		{
			MemoryScanMaterializationStatus.RuntimeInvalidated => MemoryScanFailureKind.RuntimeInvalidated,
			MemoryScanMaterializationStatus.TargetIdentityMismatch => MemoryScanFailureKind.TargetIdentityMismatch,
			_ => MemoryScanFailureKind.TargetIdentityUnavailable
		};
		throw new MemoryScanException(failureKind, operation,
			"The memory scan session cannot use its original runtime and target context for operation '" + operation +
			"'.");
	}

	private MemoryScanMaterializationStatus TryEnsureCurrentContext(LuaState state)
	{
		if (!_isBound || RuntimeIdentity != LuaRuntime.CurrentStateIdentity)
		{
			Invalidate(MemoryScanInvalidationReason.RuntimeIdentityChanged);
			return MemoryScanMaterializationStatus.RuntimeInvalidated;
		}

		if (!_targetObservation.Incarnation.HasValue)
		{
			LastTargetCheck = TargetSelection.CreateUnavailableCheck(_targetObservation);
			return MemoryScanMaterializationStatus.TargetIdentityUnavailable;
		}

		int top = state.Top;
		TargetIdentityCheck check;
		try
		{
			check = TargetSelection.ValidateCurrent(state, _targetObservation.Incarnation.GetValueOrDefault());
		}
		finally
		{
			state.SetTop(top);
		}

		LastTargetCheck = check;
		if (check.IsCurrent)
		{
			return MemoryScanMaterializationStatus.Success;
		}

		if (check.Kind is TargetIdentityCheckKind.TargetChanged or TargetIdentityCheckKind.ProcessReused)
		{
			Invalidate(check.Kind == TargetIdentityCheckKind.TargetChanged
				? MemoryScanInvalidationReason.TargetChanged
				: MemoryScanInvalidationReason.TargetProcessReused);
			return MemoryScanMaterializationStatus.TargetIdentityMismatch;
		}

		return MemoryScanMaterializationStatus.TargetIdentityUnavailable;
	}

	private void Invalidate(MemoryScanInvalidationReason reason)
	{
		if (State == MemoryScanState.Disposed)
		{
			return;
		}

		State = MemoryScanState.Invalidated;
		InvalidationReason = reason;
	}

	// Every transition first records the conservative ProtectedLuaFailure invalidation; only a transition whose native
	// calls all completed reaches this point and clears that provisional reason.
	private void CompleteTransition(MemoryScanState state)
	{
		State = state;
		InvalidationReason = MemoryScanInvalidationReason.None;
	}

	private static void CallFirstScan(LuaState state, MemScan scanner, in FirstScanRequest request)
	{
		using LuaFrame frame = new(state);
		EnumMarshaller<ScanOption>.Push(state, request.ScanOption);
		EnumMarshaller<VariableType>.Push(state, request.VariableType);
		EnumMarshaller<RoundingType>.Push(state, request.RoundingType);
		StringMarshaller.Push(state, request.Input1);
		StringMarshaller.Push(state, request.Input2);
		state.PushInteger(request.StartAddress.ToInt64());
		state.PushInteger(request.StopAddress.ToInt64());
		StringMarshaller.Push(state, request.ProtectionFlags);
		EnumMarshaller<FastScanMethod>.Push(state, request.FastScanMethod);
		StringMarshaller.Push(state, request.AlignmentParameter);
		BooleanMarshaller.Push(state, request.IsHexadecimalInput);
		BooleanMarshaller.Push(state, request.IsNotBinaryString);
		BooleanMarshaller.Push(state, request.IsUnicodeScan);
		BooleanMarshaller.Push(state, request.IsCaseSensitive);

		LuaStatus status = scanner.Handle.TryCallMethod(state, "firstScan"u8, 14, 0);
		if (!status.IsOk)
		{
			ThrowLua(state, status, FirstScanOperation);
		}
	}

	private static void CallNextScan(LuaState state, MemScan scanner, in NextScanRequest request)
	{
		using LuaFrame frame = new(state);
		EnumMarshaller<ScanOption>.Push(state, request.ScanOption);
		EnumMarshaller<RoundingType>.Push(state, request.RoundingType);
		StringMarshaller.Push(state, request.Input1);
		StringMarshaller.Push(state, request.Input2);
		BooleanMarshaller.Push(state, request.IsHexadecimalInput);
		BooleanMarshaller.Push(state, request.IsNotBinaryString);
		BooleanMarshaller.Push(state, request.IsUnicodeScan);
		BooleanMarshaller.Push(state, request.IsCaseSensitive);
		BooleanMarshaller.Push(state, request.IsPercentageScan);

		int argumentCount = 9;
		if (request.SavedResultName is not null)
		{
			StringMarshaller.Push(state, request.SavedResultName);
			argumentCount = 10;
		}

		LuaStatus status = scanner.Handle.TryCallMethod(state, "nextScan"u8, argumentCount, 0);
		if (!status.IsOk)
		{
			ThrowLua(state, status, NextScanOperation);
		}
	}

	private static void CallWaitTillDone(LuaState state, MemScan scanner)
	{
		using LuaFrame frame = new(state);
		LuaStatus status = scanner.Handle.TryCallMethod(state, "waitTillDone"u8, 0, 0);
		if (!status.IsOk)
		{
			ThrowLua(state, status, WaitForCompletionOperation);
		}
	}

	private static ulong ReadResultCount(LuaState state, FoundList foundList)
	{
		using LuaFrame frame = new(state);
		LuaStatus status = foundList.Handle.TryCallMethod(state, "getCount"u8, 0, 1);
		if (!status.IsOk)
		{
			ThrowLua(state, status, ResultCountOperation);
		}

		if (!Int64Marshaller.TryRead(state, -1, out long count) || count < 0)
		{
			throw new MemoryScanException(MemoryScanFailureKind.UnexpectedResult, ResultCountOperation,
				"The memory scan result count was not a non-negative 64-bit Lua integer.");
		}

		return (ulong) count;
	}

	private static string CallString(LuaState state, CEObject target, ReadOnlySpan<byte> method, string operation,
		int index)
	{
		using LuaFrame frame = new(state);
		state.PushInteger(index);
		LuaStatus status = target.TryCallMethod(state, method, 1, 1);
		if (!status.IsOk)
		{
			ThrowLua(state, status, operation);
		}

		if (StringMarshaller.TryRead(state, -1, out string? value))
		{
			return value;
		}

		throw new MemoryScanException(MemoryScanFailureKind.UnexpectedResult, operation,
			"The memory scan operation did not return text.");
	}

	private static void CallNoResult(LuaState state, CEObject target, ReadOnlySpan<byte> method, string operation)
	{
		using LuaFrame frame = new(state);
		LuaStatus status = target.TryCallMethod(state, method, 0, 0);
		if (!status.IsOk)
		{
			ThrowLua(state, status, operation);
		}
	}

	[DoesNotReturn]
	private static void ThrowLua(LuaState state, LuaStatus status, string operation)
	{
		LuaError error = LuaError.FromStack(state, status);
		throw new MemoryScanException(MemoryScanFailureKind.LuaError, operation,
			"The protected Lua call for memory scan operation '" + operation + "' failed.", new LuaException(error));
	}

	// What the active member must do once its CE calls have returned. Abandon takes precedence over release: it was
	// explicitly asked to make no CE call.
	private enum DeferredDisposal : byte
	{
		None = 0,
		Release = 1,
		Abandon = 2
	}
}
