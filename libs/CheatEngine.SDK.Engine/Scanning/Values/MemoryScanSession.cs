using System;
using System.Diagnostics.CodeAnalysis;
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
///         Dispose is idempotent and releases a ready found list before destroying the found-list child, then destroys the
///         scanner parent. It must run while the plugin is enabled and on the Cheat Engine main thread so the contained
///         <see cref="Owned{T}" /> values can reach <c>destroy()</c>. As with <see cref="Owned{T}" />, no finalizer runs
///         native cleanup from an arbitrary thread.
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
	private Owned<FoundList>? _foundList;
	private bool _isBound;
	private Owned<MemScan>? _scanner;
	private TargetSelectionObservation _targetObservation;

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
	[RequiresPluginEnabled]
	public MemScan Scanner
	{
		get
		{
			ThrowIfDisposed();
			RequireEnabledMainThread();
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, "MemoryScan.Scanner");
			return _scanner!.Value;
		}
	}

	/// <summary>
	///     Gets the attached found list as a borrowed handle, only after it is initialized for reading. Direct raw
	///     operations on the returned value bypass the session's state checks.
	/// </summary>
	/// <exception cref="MemoryScanStateException">The results are not ready.</exception>
	/// <exception cref="ObjectDisposedException">The session was disposed.</exception>
	[RequiresPluginEnabled]
	public FoundList Results
	{
		get
		{
			RequireState("Results", MemoryScanState.ResultsReady);
			RequireEnabledMainThread();
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, "MemoryScan.Results");
			return _foundList!.Value;
		}
	}

	/// <summary>Gets the number of readable results in the initialized found list.</summary>
	/// <remarks>
	///     Cheat Engine stores the count as <c>UInt64</c>. The Lua boundary supplies a signed 64-bit integer, so this
	///     property exposes its non-negative range as <see cref="ulong" /> and rejects an unrepresentable host result.
	///     Result-reading methods intentionally retain CE's <see cref="int" /> index parameter and therefore address
	///     only indices from zero through <c>Int32.MaxValue</c>.
	/// </remarks>
	/// <exception cref="MemoryScanStateException">The results are not ready.</exception>
	/// <exception cref="MemoryScanException">CE did not return a valid non-negative integer count.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public ulong ResultCount
	{
		get
		{
			RequireEnabledMainThread();
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			EnsureCurrentContext(operation.State, ResultCountOperation);
			return ReadResultCount(operation.State, RequireResults());
		}
	}

	/// <summary>Best-effort, no-throw disposal that consumes both owners and never implicitly retries CE cleanup.</summary>
	/// <remarks>
	///     Use <see cref="ReleaseWithOutcome" /> when the factual child and parent outcomes matter. This method is
	///     idempotent; it performs neither a target-dependent destroy on a worker nor a retry after an uncertain native
	///     destroy. It records safe refusal or an unconfirmed result in <see cref="LastReleaseOutcome" /> instead.
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
	///     This method never throws and never retries a destroy. If cleanup cannot safely begin (for example, a worker
	///     thread, detached runtime, changed Lua identity, or changed target), it consumes the managed owners through
	///     <see cref="Owned{T}.Abandon" /> and reports the refusal or unavailable cleanup rather than routing handles into
	///     a different CE context. A protected destroy failure consumes the corresponding owner and remains unconfirmed.
	/// </remarks>
	public MemoryScanReleaseOutcome ReleaseWithOutcome()
	{
		if (State == MemoryScanState.Disposed)
		{
			return LastReleaseOutcome;
		}

		try
		{
			if (LuaRuntime.IsAttached && !LuaRuntime.IsMainThread)
			{
				return ConsumeWithoutCleanup(TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure));
			}

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

	/// <summary>
	///     Stops managed cleanup without invoking CE and makes this session unusable.
	/// </summary>
	/// <remarks>
	///     This is an explicit recovery path for a session whose original Lua runtime or target cannot be validated.
	///     It deliberately does not claim that native destruction occurred, and it must not be used as normal cleanup.
	///     The found-list owner is abandoned before the scanner owner to preserve the parent/child ownership direction.
	/// </remarks>
	public void Abandon()
	{
		if (State == MemoryScanState.Disposed)
		{
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
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void StartFirstScanCancellable(in FirstScanRequest request, CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		RequireState("StartFirstScan", MemoryScanState.New);
		ValidateFirstRequest(in request);
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		EnsureCurrentContext(operation.State, FirstScanOperation);
		ThrowIfCancelledBeforeNativeCall(cancellationToken);

		// A CE error may happen after it accepts some scan setup. Do not report the old New state after a partial call.
		Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
		CallFirstScan(operation.State, _scanner!.Value, in request);
		State = MemoryScanState.Scanning;
		ObserveCancellationAfterNativeCall(cancellationToken);
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
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void StartNextScanCancellable(in NextScanRequest request, CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		RequireState("StartNextScan", MemoryScanState.ResultsReady);
		ValidateNextRequest(in request);
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		EnsureCurrentContext(operation.State, NextScanOperation);
		ThrowIfCancelledBeforeNativeCall(cancellationToken);

		// The list stops being readable as soon as this session releases it. A failed deinitialize or nextScan leaves
		// the conservative Invalidated state, from which Reset is the only recovery.
		Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
		CallNoResult(operation.State, _foundList!.Value.Handle, "deinitialize"u8, DeinitializeResultsOperation);
		CallNextScan(operation.State, _scanner!.Value, in request);
		State = MemoryScanState.Scanning;
		ObserveCancellationAfterNativeCall(cancellationToken);
	}

	/// <summary>
	///     Waits through CE's no-timeout <c>waitTillDone()</c> form, then initializes the attached found list only after
	///     CE reports completion.
	/// </summary>
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
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public void WaitForCompletionCancellable(CancellationToken cancellationToken)
	{
		LastCancellationMilestone = MemoryScanCancellationMilestone.None;
		RequireEnabledMainThread();
		RequireState("WaitForCompletion", MemoryScanState.Scanning);
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		EnsureCurrentContext(operation.State, WaitForCompletionOperation);
		ThrowIfCancelledBeforeNativeCall(cancellationToken);

		try
		{
			CallWaitTillDone(operation.State, _scanner!.Value);
			ObserveCancellationAfterNativeCall(cancellationToken);

			Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
			CallNoResult(operation.State, _foundList!.Value.Handle, "initialize"u8, InitializeResultsOperation);
			State = MemoryScanState.ResultsReady;
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

		if (State == MemoryScanState.Scanning)
		{
			ThrowWrongState("Reset");
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		EnsureCurrentContext(operation.State, ResetOperation);
		ThrowIfCancelledBeforeNativeCall(cancellationToken);

		Invalidate(MemoryScanInvalidationReason.ProtectedLuaFailure);
		CallNoResult(operation.State, _foundList!.Value.Handle, "deinitialize"u8, DeinitializeResultsOperation);
		CallNoResult(operation.State, _scanner!.Value.Handle, "newScan"u8, ResetOperation);
		State = MemoryScanState.New;
		InvalidationReason = MemoryScanInvalidationReason.None;
		ObserveCancellationAfterNativeCall(cancellationToken);
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
	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification =
			"This bounded materialization operation keeps its cancellation and ownership milestones together.")]
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

	private MemoryScanReleaseOutcome ReleaseWithinCurrentContext(LuaState state)
	{
		Owned<FoundList>? foundList = _foundList;
		Owned<MemScan>? scanner = _scanner;
		if (State == MemoryScanState.ResultsReady && foundList is not null && !foundList.IsDisposed)
		{
			using LuaFrame frame = new(state);
			_ = foundList.Value.Handle.TryCallMethod(state, "deinitialize"u8, 0, 0);
		}

		TargetReleaseOutcome foundListOutcome = ReleaseOwned(state, foundList);
		TargetReleaseOutcome scannerOutcome = ReleaseOwned(state, scanner);
		return CompleteRelease(foundListOutcome, scannerOutcome);
	}

	private MemoryScanReleaseOutcome ConsumeWithoutCleanup(TargetReleaseOutcome outcome)
	{
		ConsumeOwner(_foundList);
		ConsumeOwner(_scanner);
		return CompleteRelease(outcome, outcome);
	}

	private MemoryScanReleaseOutcome CompleteRelease(TargetReleaseOutcome foundListOutcome,
		TargetReleaseOutcome scannerOutcome)
	{
		_foundList = null;
		_scanner = null;
		State = MemoryScanState.Disposed;
		LastReleaseOutcome = new MemoryScanReleaseOutcome(foundListOutcome, scannerOutcome, true, true);
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
}
