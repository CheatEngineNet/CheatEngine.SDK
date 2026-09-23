using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Protected;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.State;

using static System.Threading.Volatile;

namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     The ambient binding between this SDK copy and its host: one per loaded <c>CheatEngine.SDK.Lua</c> assembly
///     instance, owned by this assembly so that <c>CheatEngine.SDK.Engine</c> and generated code can reach the host's Lua
///     state without referencing <c>CheatEngine.SDK.Hosting</c> or the ABI. Whether that equals one per plugin is decided
///     by the host loader profile, not assumed here (Q09: C2 host-emulator facts, C4 receipts).
/// </summary>
/// <remarks>
///     <para>
///         <b>Lifecycle.</b> <c>CheatEngine.SDK.Hosting</c> calls <see cref="Attach" /> in the enable callback, after
///         binding <c>CheatEngine.SDK.Lua.Interop.Api.LuaApi</c>, and <see cref="Detach" /> in the disable callback.
///         Every attach advances <see cref="Epoch" />, which invalidates every <see cref="References.LuaRef" /> created
///         before it. A
///         supported in-place state replacement instead advances <see cref="StateGeneration" /> while retaining its
///         attachment epoch; persistent resources compare both values. Detach neutralizes and frees every live
///         <see cref="Callbacks.LuaCallback" /> while the state can still be reached, so no managed state is ever freed
///         behind a closure Lua can still call. Internal host-subscription owners are first made inert and then
///         unregistered while their old state remains reachable; this does not expose a timer or hotkey API.
///     </para>
///     <para>
///         <b>Acquiring a state.</b> Cheat Engine hands out one Lua thread per OS thread. Start every normal operation
///         with <see cref="AcquireOperation()" /> and use its <see cref="LuaRuntimeOperation.State" /> only for that
///         synchronous scope. The admission remains held from before the provider call through the last Lua operation,
///         so a lifecycle transition can close new work and drain existing work before it invalidates resources. Inside
///         a callback the state the callback received is authoritative; do not acquire another. A state must never be
///         stored.
///     </para>
///     <para>
///         <b>Thread safety.</b> Attach and Detach are serialized by a lock and normally run on the host's main thread;
///         the
///         readers (<see cref="IsAttached" />, <see cref="Epoch" />, <see cref="AcquireOperation()" />, ...) are lock-free
///         volatile
///         reads and may run on any thread. A reader that observes the binding while Detach runs completes with the
///         binding
///         it read; the host guarantees that the provider stays callable until the disable callback returns.
///         Admission protects this SDK copy's attach/reset/detach transition; it is not a process-wide Lua mutex.
///         Distinct states from two worker threads can be coroutines of one shared Lua heap, so callers still need the
///         host's proven serialization policy before overlapping arbitrary Lua API work. The SDK neither infers that
///         policy from pointer inequality nor claims a live concurrency qualification.
///     </para>
///     <para>
///         <b>
///             Threading and Lua concurrency contract (ADR-07, unqualified pending a future host-based validation run; no
///             local CE qualification is currently available).
///         </b>
///         The 2.0 default is <see cref="LuaThreadAdmission.MainThreadOnly" />:
///         a new <see cref="AcquireOperation()" />-family call is refused with
///         <see cref="LuaAdmissionStatus.ThreadNotAdmitted" />
///         before the host's state provider ever runs, unless the calling thread is the host's captured main thread, the
///         call is nested inside a Lua operation or callback already admitted on that thread, or it is the single
///         documented default exception: the worker-side <c>synchronize</c> hand-off behind
///         <c>CheatEngine.SDK.Hosting.Threading.MainThread.Invoke</c>. A
///         plugin opts a worker thread in with the experimental <see cref="AdmitWorkerThreads" />
///         (gate id <c>CESDK5001</c>), unqualified until Q19 passes at C3 <b>and</b> C4. Admission protects only
///         this SDK copy's attach/reset/detach transitions, never the shared Lua heap: two plugins, or two SDK copies, are
///         never serialized by the SDK (A05-07, A08-05, F04). See <c>CheatEngine.SDK.Hosting</c>'s README, section
///         "Threading and Lua concurrency contract (ADR-07)", for the complete written contract, including the pluginCS
///         hazard, <c>processMessages</c> re-entrancy, and the "not qualified" list.
///     </para>
///     <para>
///         <b>State replacement (2.0 decision).</b> A reset must first call the SDK-controlled preparation path
///         (<see cref="BeginStateReset" />) while the old state is reachable. That path neutralizes rooted callbacks and
///         invalidates state-bound resources before the host replaces the state. Calling Cheat Engine's
///         <c>resetLuaState</c> outside that path remains unsupported, but is no longer silently accepted: the runtime
///         stamps every attachment with a private registry marker and re-checks it on every admitted provider
///         acquisition. A mismatch is deterministic <i>detection</i> of an external reset — never a guess at whether an
///         old registry slot or callback closure remains valid: <see cref="ExternalStateResetDetected" /> becomes
///         <see langword="true" />, every admission path refuses with <see cref="LuaAdmissionStatus.ExternalStateReset" />
///         until the next <see cref="Attach" />, old owners are refused deterministically, and nothing is ever released
///         into the replacement registry (A08-22). There is no public reset API in 2.0 (O2): an SDK-owned reset in one
///         plugin cannot neutralize another plugin's owners of the same shared Lua universe.
///     </para>
/// </remarks>
public static unsafe class LuaRuntime
{
	private static readonly Lock SGate = new();
	private static readonly Lock SOperationGate = new();
	private static readonly ManualResetEventSlim SOperationsDrained = new(true);

	private static LuaHostServices? s_services;
	private static int s_activeOperations;
	private static bool s_acceptOperations;

	private static bool s_resetTransitionActive;

	// High 32 bits: attach epoch. Low 32 bits: state generation. A single volatile read never combines either component
	// from different lifecycle transitions.
	private static long s_identity;

	// 2.0 conservative-by-default worker-thread admission policy (F04, ADR-07). Reset to MainThreadOnly by every Attach
	// and Detach, and by the first external-reset detection, so every enable starts conservative.
	private static int s_threadAdmission;

	// The private-registry universe stamp (WI-3 / A08-21, A08-22, A20-Q17-3, A20-Q18-2). Key is unique per SDK copy and
	// never moves; s_stamped guards the one-time write per attachment so later admissions only verify it.
	private static int s_stamped;
	private static int s_externalResetDetected;

	// "Raised at most once per attachment" gates for the one-shot diagnostics (A24 category tokens), reset by Attach.
	private static int s_workerThreadRefusedRaised;

	// Set by CheatEngine.SDK.Hosting while attached; cleared in its cleanup. Invoked outside every runtime lock.
	internal static Action<LuaRuntimeDiagnostic>? DiagnosticObserver;

	// A transition owner is allowed to release callbacks and references after admission has closed. An active Lua
	// operation is never allowed to start a transition: doing so would wait for itself and deadlock.
	[ThreadStatic] private static int t_operationDepth;

	[ThreadStatic] private static int t_transitionDepth;

	// Deterministic lifecycle-race seam used only by the SDK's friend test assembly. It is invoked after admission is
	// closed and before the drain wait, outside every runtime lock.
	internal static Action? OperationAdmissionClosedForTesting;

	/// <summary>The universe stamp's private-registry key. For tests only (distinctness from other private keys).</summary>
	internal static nint StampKeyForTests
	{
		get;
	} = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(LuaRuntime), 1);

	/// <summary>Gets a value indicating whether a host binding is attached. Lock-free; any thread.</summary>
	public static bool IsAttached => Read(ref s_services) is not null;

	/// <summary>
	///     Gets whether the calling thread currently owns an admitted Lua operation. Internal lifecycle code uses this
	///     preflight before taking another lifecycle lock, so an invalid nested transition is rejected without waiting
	///     behind a transition that is draining this thread's lease.
	/// </summary>
	internal static bool IsOperationAdmittedOnCurrentThread => t_operationDepth != 0;

	/// <summary>
	///     Gets the attach counter: 0 before the first <see cref="Attach" />, incremented by every attach, unchanged by
	///     <see cref="Detach" /> or a supported state replacement. This compatibility property is the attach component
	///     of <see cref="CurrentStateIdentity" />; persistent Lua resources must compare the complete identity.
	///     Lock-free; any thread.
	/// </summary>
	public static int Epoch
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => CurrentStateIdentity.AttachEpoch;
	}

	/// <summary>
	///     Gets the Lua state replacement counter. It changes only when the SDK prepares a supported state replacement,
	///     not when the host attaches or detaches. Lock-free; any thread.
	/// </summary>
	public static int StateGeneration
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => CurrentStateIdentity.StateGeneration;
	}

	/// <summary>
	///     Gets the atomically captured pair of the current attachment epoch and state generation. A persistent Lua
	///     resource is usable only while both components match the value it captured. Lock-free; any thread.
	/// </summary>
	public static LuaStateIdentity CurrentStateIdentity
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => UnpackIdentity(Read(ref s_identity));
	}

	/// <summary>
	///     Gets a value indicating whether the calling thread is the host's main thread. <see langword="false" /> while
	///     detached.
	/// </summary>
	public static bool IsMainThread
	{
		get
		{
			LuaHostServices? services = Read(ref s_services);
			return services is not null && services.MainThreadId == Environment.CurrentManagedThreadId;
		}
	}

	/// <summary>Gets the attached binding, or <see langword="default" /> while detached.</summary>
	public static LuaHostBinding CurrentBinding => Read(ref s_services)?.Binding ?? default;

	/// <summary>
	///     Gets the current worker-thread admission policy. Reading this is harmless and never gated; only
	///     <see cref="AdmitWorkerThreads" /> is <c>[Experimental]</c>. Lock-free; any thread.
	/// </summary>
	public static LuaThreadAdmission ThreadAdmission
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => (LuaThreadAdmission) Read(ref s_threadAdmission);
	}

	/// <summary>
	///     Gets a value indicating whether this SDK copy detected that the host replaced its Lua state outside the
	///     SDK-controlled reset path (<see cref="BeginStateReset" />). Sticky until the next <see cref="Attach" />.
	///     Lock-free; any thread.
	/// </summary>
	public static bool ExternalStateResetDetected
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Read(ref s_externalResetDetected) != 0;
	}

	/// <summary>
	///     Acquires a Lua state together with a lifecycle admission that spans the whole synchronous operation.
	/// </summary>
	/// <returns>The admitted operation. Dispose it before returning to the host.</returns>
	/// <exception cref="InvalidOperationException">
	///     No binding is attached, the host returned no state for the calling thread, or a lifecycle transition has
	///     already closed admission for new work.
	/// </exception>
	/// <remarks>
	///     Use <c>using var operation = LuaRuntime.AcquireOperation(); var state = operation.State;</c>. The provider is
	///     called exactly once after admission succeeds. This method does not allocate and must not be used across an
	///     <see langword="await" />.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaRuntimeOperation AcquireOperation()
	{
		LuaAdmissionStatus status = TryEnterProviderOperationWithOutcome(out LuaState state, false);
		if (status == LuaAdmissionStatus.Admitted)
		{
			return new LuaRuntimeOperation(state, true);
		}

		ThrowForAdmissionStatus(status);
		return default;
	}

	/// <summary>Non-throwing <see cref="AcquireOperation()" />.</summary>
	/// <param name="operation">The admitted operation on success; default otherwise.</param>
	/// <returns>
	///     <see langword="true" /> when a binding is attached, admission is open, the calling thread is admitted and the
	///     host provided a state for it.
	/// </returns>
	[RequiresPluginEnabled]
	public static bool TryAcquireOperation(out LuaRuntimeOperation operation)
	{
		LuaAdmissionStatus status = TryEnterProviderOperationWithOutcome(out LuaState state, false);
		if (status == LuaAdmissionStatus.Admitted)
		{
			operation = new LuaRuntimeOperation(state, true);
			return true;
		}

		operation = default;
		return false;
	}

	/// <summary>
	///     Non-throwing <see cref="AcquireOperation()" /> that reports the factual admission reason instead of a boolean.
	/// </summary>
	/// <param name="operation">
	///     The admitted operation when the result is <see cref="LuaAdmissionStatus.Admitted" />; default
	///     otherwise.
	/// </param>
	/// <returns>The factual admission outcome. Never derive a reason from an exception message instead of this enum.</returns>
	[RequiresPluginEnabled]
	public static LuaAdmissionStatus TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation)
	{
		LuaAdmissionStatus status = TryEnterProviderOperationWithOutcome(out LuaState state, false);
		operation = status == LuaAdmissionStatus.Admitted ? new LuaRuntimeOperation(state, true) : default;
		return status;
	}

	/// <summary>
	///     Acquires an operation for <see cref="LuaCallback.Dispose()" /> and reports the transition or admission reason
	///     that refused it, rather than a raw unavailable state.
	/// </summary>
	/// <remarks>
	///     The result is selected while <c>SOperationGate</c> is held. In particular, an
	///     <see cref="LuaCallbackDisposeOperationResult.AdmissionClosed" /> result cannot be reinterpreted as detached
	///     after an unsuccessful transition reopens admission: callback disposal must leave registry ownership with that
	///     transition until a state has neutralized the Lua closure.
	///     <see cref="LuaCallbackDisposeOperationResult.ThreadNotAdmitted" />
	///     and <see cref="LuaCallbackDisposeOperationResult.ExternalStateReset" /> are treated the same way by callers:
	///     never abandon the closure early from a thread or a universe the SDK does not currently trust.
	/// </remarks>
	internal static LuaCallbackDisposeOperationResult TryAcquireOperationForCallbackDispose(
		out LuaRuntimeOperation operation)
	{
		LuaAdmissionStatus status = TryEnterProviderOperationWithOutcome(out LuaState state, false);
		switch (status)
		{
			case LuaAdmissionStatus.Admitted:
				operation = new LuaRuntimeOperation(state, true);
				return LuaCallbackDisposeOperationResult.Acquired;
			case LuaAdmissionStatus.TransitionInProgress:
				operation = default;
				return LuaCallbackDisposeOperationResult.AdmissionClosed;
			case LuaAdmissionStatus.ThreadNotAdmitted:
				operation = default;
				return LuaCallbackDisposeOperationResult.ThreadNotAdmitted;
			case LuaAdmissionStatus.ExternalStateReset:
				operation = default;
				return LuaCallbackDisposeOperationResult.ExternalStateReset;
			default:
				operation = default;
				return LuaCallbackDisposeOperationResult.Unavailable;
		}
	}

	/// <summary>
	///     Opts the calling attachment into admitting Lua operations on worker threads that are not already running
	///     admitted Lua work. Reverted to <see cref="LuaThreadAdmission.MainThreadOnly" /> by every
	///     <see cref="Attach" />, <see cref="Detach" /> and external-reset detection, so call it again from
	///     <c>OnEnable</c> every time.
	/// </summary>
	/// <exception cref="InvalidOperationException">No binding is attached.</exception>
	/// <remarks>
	///     Unqualified; ADR-07. Q19 must pass at C3 <b>and</b> C4 before this gate is lifted — no local CE qualification
	///     is currently available, so both remain <see cref="LuaAdmissionStatus.Unknown" /> in this SDK copy today.
	///     Opting in admits worker-thread <see cref="AcquireOperation()" />-family calls; it does not serialize the
	///     shared Lua heap, and it does not qualify the worker-side <c>synchronize</c> hand-off heap safety or
	///     multi-plugin concurrency (see the Hosting README "Not qualified" list).
	/// </remarks>
	[Experimental("CESDK5001",
		UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md")]
	public static void AdmitWorkerThreads()
	{
		if (Read(ref s_services) is null)
		{
			ThrowDetached();
		}

		Write(ref s_threadAdmission, (int) LuaThreadAdmission.WorkerThreads);
	}

	/// <summary>Reverts to the conservative <see cref="LuaThreadAdmission.MainThreadOnly" /> default.</summary>
	public static void AdmitMainThreadOnly()
	{
		Write(ref s_threadAdmission, (int) LuaThreadAdmission.MainThreadOnly);
	}

	/// <summary>
	///     Acquires lifecycle admission for synchronous work that already has the Lua state supplied by the host, such
	///     as a generated binding with a leading <see cref="LuaState" /> parameter.
	/// </summary>
	/// <param name="state">The calling thread's Lua state.</param>
	/// <returns>The admitted operation. Dispose it after the last operation on <paramref name="state" />.</returns>
	/// <exception cref="InvalidOperationException">
	///     No binding is attached or the runtime is closing admission for a lifecycle transition.
	/// </exception>
	/// <remarks>
	///     This overload does not call the host state provider. It is re-entrant on a thread that already owns an
	///     operation: the returned non-owning lease preserves <paramref name="state" /> but relies on the outer lease,
	///     so nested generated helpers cannot release admission before their caller has restored its stack. Do not use
	///     it across <see langword="await" />.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaRuntimeOperation AcquireOperation(LuaState state)
	{
		if (state.IsNull)
		{
			throw new ArgumentException("A supplied Lua operation state cannot be null.", nameof(state));
		}

		if (Read(ref s_services) is null)
		{
			ThrowDetached();
		}

		if (Read(ref s_externalResetDetected) != 0)
		{
			ThrowExternalStateReset();
		}

		// Only the lifecycle code itself may use a state while it has made the transition exclusive. A public
		// generated binding must never inherit that privilege merely because it happened to run synchronously from
		// host cleanup code.
		if (t_transitionDepth != 0)
		{
			ThrowOperationAdmissionClosed();
		}

		if (t_operationDepth != 0)
		{
			return new LuaRuntimeOperation(state, false);
		}

		if (TryEnterOperation())
		{
			return new LuaRuntimeOperation(state, true);
		}

		ThrowOperationAdmissionClosed();
		return default;
	}

	/// <summary>
	///     The single documented default exception to <see cref="LuaThreadAdmission.MainThreadOnly" />: the worker-side
	///     half of CE's <c>synchronize</c> hand-off (<c>CheatEngine.SDK.Hosting.Threading.MainThreadDispatcher.Dispatch</c>).
	///     Admits the calling thread regardless of the current <see cref="ThreadAdmission" /> policy.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     No binding is attached, the host returned no state for the calling thread, a lifecycle transition has closed
	///     admission, or an external reset was detected.
	/// </exception>
	/// <remarks>
	///     This is CE's designed cross-thread primitive, fixed and SDK-owned, never arbitrary plugin Lua: one Lua global
	///     read, one closure, one protected call on the worker's own coroutine. It is why <c>MainThread.Invoke</c> from a
	///     worker keeps working unchanged under the 2.0 conservative default (Q19 C3/C4 scope includes this hand-off's
	///     heap safety, not its admission).
	/// </remarks>
	internal static LuaRuntimeOperation AcquireOperationForMainThreadDispatch()
	{
		LuaAdmissionStatus status = TryEnterProviderOperationWithOutcome(out LuaState state, true);
		if (status == LuaAdmissionStatus.Admitted)
		{
			return new LuaRuntimeOperation(state, true);
		}

		ThrowForAdmissionStatus(status);
		return default;
	}

	/// <summary>
	///     Pushes one generated <c>[LuaFunction]</c> closure and captures the current attachment identity in that
	///     closure. Stack: +1 (the function) on success; +1 (the error value) on failure.
	/// </summary>
	/// <param name="state">The calling thread's Lua state.</param>
	/// <param name="thunk">The generated unmanaged thunk to wrap.</param>
	/// <returns>The status of installing the Lua error wrapper and guarded closure.</returns>
	/// <remarks>
	///     The LuaBindings generator is the normal caller. Each invocation stores the current attachment epoch and
	///     state generation as closure upvalues. Consequently, a Lua script retaining a function value after disable,
	///     state reset or re-enable receives an ordinary Lua error instead of reaching the old generated scope. A
	///     detached native-fixture registration remains supported for existing direct-runtime tests; it is explicitly
	///     marked as not requiring an attachment and must not be used as a host lifecycle integration path.
	/// </remarks>
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	public static LuaStatus TryPushGeneratedFunction(LuaState state, LuaNativeFunction thunk)
	{
		using LuaRuntimeOperation operation = EnterStateOperation(state);
		LuaStateIdentity identity = CurrentStateIdentity;
		bool requiresAttachedRuntime = Read(ref s_services) is not null;
		return state.TryPushGeneratedFunction(thunk, identity, requiresAttachedRuntime);
	}

	/// <summary>
	///     Publishes a host binding and advances <see cref="Epoch" />. Called by the host's enable callback; when a binding
	///     is already attached it is replaced (its live callbacks are neutralized first, as in <see cref="Detach" />).
	/// </summary>
	/// <param name="binding">The binding; its state provider must not be zero.</param>
	/// <exception cref="ArgumentException"><paramref name="binding" /> has no state provider.</exception>
	/// <remarks>
	///     Requires <c>CheatEngine.SDK.Lua.Interop.Api.LuaApi</c> to be bound already (asserted in Debug builds): every
	///     operation after this call goes through it.
	/// </remarks>
	public static void Attach(in LuaHostBinding binding)
	{
		if (!binding.IsValid)
		{
			throw new ArgumentException("The host binding has no Lua state provider.", nameof(binding));
		}

		Debug.Assert(LuaApi.IsInitialized,
			"CheatEngine.SDK.Lua.Interop.Api.LuaApi must be bound before the runtime is attached.");

		ThrowIfTransitionFromCurrentOperation();
		lock (SGate)
		{
			ThrowIfResetTransitionActive();
			CloseOperationAdmissionAndDrain();
			BeginTransition();
			try
			{
				ReleasePreviousBindingForAttach(s_services);
				PublishNewBindingForAttach(in binding);
			}
			finally
			{
				EndTransition();
				// If replacement cleanup failed, s_services still names the previous usable binding. Reopen it rather
				// than stranding every caller behind the admission gate until a later lifecycle call happens to retry.
				OpenOperationAdmission();
				if (s_services != null)
				{
					LuaHostSubscriptionRegistry.OpenRegistrationAdmission();
				}
			}
		}
	}

	// Only called from inside Attach's SGate, while replacing an already-attached binding.
	private static void ReleasePreviousBindingForAttach(LuaHostServices? previous)
	{
		if (previous is null)
		{
			return;
		}

		if (DetectExternalResetForDetach(previous, out LuaState previousState))
		{
			LuaHostSubscriptionRegistry.AbandonAll();
			LuaCallbackRegistry.AbandonAll();
		}
		else
		{
			LuaHostSubscriptionRegistry.DetachAll(previousState);
			LuaCallbackRegistry.DetachAll(previous);
		}
	}

	// Only called from inside Attach's SGate.
	private static void PublishNewBindingForAttach(in LuaHostBinding binding)
	{
		LuaHostServices newServices = new(in binding);
		lock (LuaReferences.Gate)
		{
			LuaStateIdentity identity = CurrentStateIdentity;
			PublishIdentity(unchecked(identity.AttachEpoch + 1), identity.StateGeneration);
			Write(ref s_services, newServices);
			LuaHostSubscriptionRegistry.OpenRegistrationAdmission();
		}

		// Every enable starts conservative and with a clean slate: the previous attachment's one-shot diagnostics,
		// universe stamp and external-reset flag never leak into the new one.
		Write(ref s_threadAdmission, (int) LuaThreadAdmission.MainThreadOnly);
		Write(ref s_workerThreadRefusedRaised, 0);
		Write(ref s_externalResetDetected, 0);
		Write(ref s_stamped, 0);
		StampIfStateAvailable(newServices);
	}

	/// <summary>
	///     Begins the exclusive SDK side of a supported Lua-state replacement. The returned transition keeps operation
	///     admission closed until its <see cref="LuaStateResetTransition.Dispose" /> method completes.
	/// </summary>
	/// <remarks>
	///     This internal protocol is intentionally unavailable until the SDK owns the protected CE
	///     <c>resetLuaState</c> invocation end to end. Call it while the old state is reachable, perform the host reset,
	///     then dispose the returned transition. It first rejects new operations and drains admitted operations, then
	///     neutralizes callbacks and advances <see cref="StateGeneration" />. A reset attempted from an admitted Lua
	///     operation is rejected rather than waiting for itself. If the host reset fails, dispose still reopens admission;
	///     the advanced generation conservatively abandons old state resources.
	/// </remarks>
	internal static LuaStateResetTransition BeginStateReset()
	{
		ThrowIfTransitionFromCurrentOperation();
		lock (SGate)
		{
			ThrowIfResetTransitionActive();
			LuaHostServices? services = s_services;
			if (services is null)
			{
				ThrowDetached();
			}

			CloseOperationAdmissionAndDrain();
			BeginTransition();
			try
			{
				if (DetectExternalResetForDetach(services, out LuaState state))
				{
					LuaHostSubscriptionRegistry.AbandonAll();
					LuaCallbackRegistry.AbandonAll();
				}
				else
				{
					LuaHostSubscriptionRegistry.DetachAll(state);
					LuaCallbackRegistry.DetachAll(services);
				}

				lock (LuaReferences.Gate)
				{
					LuaStateIdentity identity = CurrentStateIdentity;
					PublishIdentity(identity.AttachEpoch, unchecked(identity.StateGeneration + 1));
				}

				s_resetTransitionActive = true;
				return new LuaStateResetTransition(true);
			}
			catch
			{
				EndTransition();
				OpenOperationAdmission();
				LuaHostSubscriptionRegistry.OpenRegistrationAdmission();
				throw;
			}
		}
	}

	/// <summary>
	///     Withdraws the host binding. Called by the host's disable callback while the provider is still valid: every
	///     live <see cref="LuaCallback" /> is neutralized on the Lua side and its managed state freed before the binding
	///     goes away. Idempotent. Does not change <see cref="Epoch" />.
	/// </summary>
	public static void Detach()
	{
		ThrowIfTransitionFromCurrentOperation();
		lock (SGate)
		{
			ThrowIfResetTransitionActive();
			LuaHostServices? services = s_services;
			if (services is null)
			{
				return;
			}

			CloseOperationAdmissionAndDrain();
			BeginTransition();
			bool detachSucceeded = false;
			try
			{
				// After a detected external reset, services.Provider() would hand out the replacement VM's state: any
				// unref or unregister against it would corrupt a registry this SDK copy never created (A08-22). Abandon
				// makes no Lua call at all instead.
				if (DetectExternalResetForDetach(services, out LuaState state))
				{
					LuaHostSubscriptionRegistry.AbandonAll();
					LuaCallbackRegistry.AbandonAll();
				}
				else
				{
					LuaHostSubscriptionRegistry.DetachAll(state);
					LuaCallbackRegistry.DetachAll(services);
				}

				Write(ref s_services, null);
				// The next enable starts conservative: a detached runtime never remembers an opt-in from before.
				Write(ref s_threadAdmission, (int) LuaThreadAdmission.MainThreadOnly);
				detachSucceeded = true;
			}
			finally
			{
				EndTransition();
				// A callback release can fail (for example, while Lua rejects a registry operation). Keep the binding
				// and its remaining registry entries reachable so a caller can retry Detach after that failure clears.
				if (!detachSucceeded)
				{
					OpenOperationAdmission();
					LuaHostSubscriptionRegistry.OpenRegistrationAdmission();
				}
			}
		}
	}

	/// <summary>
	///     Asks the host for the Lua state of the calling thread without retaining a lifecycle operation admission.
	/// </summary>
	/// <returns>The state view; never <see cref="LuaState.IsNull" />.</returns>
	/// <exception cref="InvalidOperationException">
	///     No binding is attached (the plugin is not enabled), the host returned no state for this thread, or a lifecycle
	///     transition is in progress.
	/// </exception>
	/// <remarks>
	///     This legacy escape hatch is unsafe across attach, detach and Lua-state replacement because the returned value
	///     carries no deterministic release point for a lifecycle admission. Normal SDK and generated code must use
	///     <see cref="AcquireOperation()" /> instead. It remains only for advanced code that is already externally
	///     serialized with the host lifecycle (for example, a Lua callback receiving its own state).
	/// </remarks>
	[RequiresPluginEnabled]
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	public static LuaState AcquireState()
	{
		using LuaRuntimeOperation operation = AcquireOperation();
		return operation.State;
	}

	/// <summary>Non-throwing advanced-unsafe counterpart of <see cref="AcquireState" />.</summary>
	/// <param name="state">The state view, or <see cref="LuaState.IsNull" /> on failure.</param>
	/// <returns><see langword="false" /> while detached or when the host returned no state for this thread.</returns>
	public static bool TryAcquireState(out LuaState state)
	{
		if (!TryAcquireOperation(out LuaRuntimeOperation operation))
		{
			state = default;
			return false;
		}

		try
		{
			state = operation.State;
			return true;
		}
		finally
		{
			operation.Dispose();
		}
	}

	/// <summary>
	///     Pushes the host's userdata for a native object (Cheat Engine's <c>LuaPushClassInstance</c>): the only way a
	///     host object gets onto the stack. Stack: +1.
	/// </summary>
	/// <param name="state">The state to push on; the calling thread's.</param>
	/// <param name="nativeObject">
	///     The native object pointer as the host knows it; zero is passed through and is the host's
	///     business.
	/// </param>
	/// <exception cref="InvalidOperationException">No binding is attached, or the binding has no pusher.</exception>
	/// <remarks>
	///     The pusher runs beneath the native bridge's <c>lua_pcallk</c> protection. If creating the userdata or its
	///     metatable raises a Lua error, the bridge returns that status and this method translates the error object on
	///     the stack through the normal protected-result path; a Lua <c>longjmp</c> never crosses a managed frame.
	/// </remarks>
	[RequiresPluginEnabled]
	[LuaStackEffect(1)]
	public static void PushHostObject(LuaState state, nint nativeObject)
	{
		using LuaRuntimeOperation operation = EnterStateOperation(state);
		LuaHostServices? services = Read(ref s_services);
		if (services is null)
		{
			ThrowDetached();
		}

		if (services.Pusher is null)
		{
			ThrowNoPusher();
		}

		state.CheckProtectedResult(new LuaStatus(
			LuaProtectedApi.PushHostObject(state.Pointer, (nint) services.Pusher, nativeObject)));
	}

	/// <summary>
	///     Begins a lifecycle admission for an operation that already owns a Lua state, such as callback construction or
	///     a private-reference operation. The transition owner and detached native-fixture callers need no admission.
	/// </summary>
	/// <remarks>
	///     The caller must dispose the returned value after its last Lua operation. This is internal so ordinary callers
	///     acquire both the state and its admission through <see cref="AcquireOperation()" />. A raw state obtained through
	///     the advanced <see cref="AcquireState" /> escape hatch remains the caller's responsibility across a host
	///     lifecycle transition.
	/// </remarks>
	internal static LuaRuntimeOperation EnterStateOperation(LuaState state)
	{
		if (t_transitionDepth != 0 || t_operationDepth != 0 || Read(ref s_services) is null)
		{
			return default;
		}

		if (!TryEnterOperation())
		{
			ThrowOperationAdmissionClosed();
		}

		return new LuaRuntimeOperation(state, true);
	}

	/// <summary>
	///     Attempts to admit one invocation of a managed Lua callback. A callback begun before a lifecycle transition
	///     keeps the gate until its unmanaged thunk returns; one begun after admission has closed is rejected before
	///     plugin code is entered.
	/// </summary>
	/// <param name="operation">The lease held for this callback invocation when the runtime is attached.</param>
	/// <returns>
	///     <see langword="false" /> only when an attached runtime is closing admission. Detached native-fixture
	///     callbacks remain supported and return <see langword="true" /> with a default lease.
	/// </returns>
	internal static bool TryEnterCallbackOperation(out LuaRuntimeOperation operation)
	{
		if (Read(ref s_externalResetDetected) != 0)
		{
			operation = default;
			return false;
		}

		// A callback that is re-entered by an already admitted Lua operation shares that outer lease. The lifecycle
		// transition owner is different: admitting plugin code there would let a finalizer/metamethod re-enter after
		// CloseOperationAdmissionAndDrain has established exclusive cleanup.
		if (t_transitionDepth != 0 && Read(ref s_services) is not null)
		{
			operation = default;
			return false;
		}

		if (t_operationDepth != 0 || Read(ref s_services) is null)
		{
			operation = default;
			return true;
		}

		if (TryEnterOperation())
		{
			operation = new LuaRuntimeOperation(default, true);
			return true;
		}

		operation = default;
		return false;
	}

	/// <summary>
	///     Closes admission for every internal host-subscription callback and drains callbacks already admitted. Hosting
	///     calls this before plugin <c>OnDisable</c>, while Lua remains attached; <see cref="Detach" /> later owns the
	///     state-bound unregister attempt. This is not a timer or hotkey capability surface.
	/// </summary>
	internal static void CloseHostSubscriptionAdmissionAndDrain()
	{
		ThrowIfTransitionFromCurrentOperation();
		if (Read(ref s_services) is null)
		{
			return;
		}

		LuaHostSubscriptionRegistry.CloseCallbackAdmissionAndDrain();
	}

	// A generated [LuaFunction] closure stores this pair in Lua upvalues. Keeping the test and the following
	// admission attempt distinct is intentional: once admission closes, a closure that raced with disable is rejected
	// before user code; one admitted before the boundary retains its lease until its unmanaged thunk returns.
	internal static bool IsGeneratedFunctionRegistrationCurrent(int attachEpoch, int stateGeneration)
	{
		return Read(ref s_services) is not null
			   && Read(ref s_identity) == PackIdentity(attachEpoch, stateGeneration)
			   && IsOperationAdmissionOpen();
	}

	/// <summary>
	///     Closes admission for new <see cref="AcquireOperation()" /> calls and waits for all admitted operations to leave.
	///     This is the host lifecycle seam immediately before disable cleanup. It is idempotent while admission is closed.
	/// </summary>
	/// <remarks>
	///     This method must not run from an admitted Lua operation: waiting would deadlock on that operation's own lease.
	///     It deliberately does not hold the admission gate while waiting or while the caller neutralizes callbacks.
	/// </remarks>
	internal static void CloseOperationAdmissionAndDrain()
	{
		ThrowIfTransitionFromCurrentOperation();

		lock (SOperationGate)
		{
			s_acceptOperations = false;
		}

		Read(ref OperationAdmissionClosedForTesting)?.Invoke();
		SOperationsDrained.Wait();
	}

	internal static void CompleteStateReset()
	{
		lock (SGate)
		{
			if (!s_resetTransitionActive || t_transitionDepth == 0)
			{
				throw new InvalidOperationException(
					"The Lua state-reset transition must be completed on its owning thread.");
			}

			s_resetTransitionActive = false;
			EndTransition();
			OpenOperationAdmission();
			LuaHostServices? services = Read(ref s_services);
			if (services is not null)
			{
				LuaHostSubscriptionRegistry.OpenRegistrationAdmission();

				// The replacement state is a fresh registry: re-stamp it so later admissions verify against this
				// generation, not the one the old universe carried.
				Write(ref s_stamped, 0);
				StampIfStateAvailable(services);
			}
		}
	}

	internal static void ExitOperation()
	{
		if (t_operationDepth <= 0)
		{
			throw new InvalidOperationException(
				"A Lua runtime operation must be disposed on the thread that acquired it.");
		}

		t_operationDepth--;
		lock (SOperationGate)
		{
			if (--s_activeOperations == 0)
			{
				SOperationsDrained.Set();
			}
		}
	}

	// Evaluates the 2.0 admission policy under SOperationGate, entirely before services.Provider() ever runs on a
	// refused thread (pitfall 2): detached, external reset, transitioning and thread-not-admitted are all decided
	// without creating a coroutine. Only then is the provider called and the universe stamp verified.
	private static LuaAdmissionStatus TryEnterProviderOperationWithOutcome(out LuaState state,
		bool bypassThreadAdmission)
	{
		LuaAdmissionStatus gateStatus = TryEnterAdmissionGate(bypassThreadAdmission, out LuaHostServices? services);
		if (gateStatus != LuaAdmissionStatus.Admitted)
		{
			state = default;
			if (gateStatus == LuaAdmissionStatus.ThreadNotAdmitted)
			{
				RaiseWorkerThreadRefusedOnce();
			}

			return gateStatus;
		}

		try
		{
			lua_State* l = services!.Provider();
			if (l is null)
			{
				ExitOperation();
				state = default;
				return LuaAdmissionStatus.NoStateForThread;
			}

			LuaState acquired = new(l);
			if (!TryStampOrVerify(acquired))
			{
				ExitOperation();
				state = default;
				ReportExternalReset();
				return LuaAdmissionStatus.ExternalStateReset;
			}

			state = acquired;
			return LuaAdmissionStatus.Admitted;
		}
		catch
		{
			ExitOperation();
			throw;
		}
	}

	// The admission decision, entirely under SOperationGate and entirely before services.Provider() ever runs
	// (pitfall 2): detached, external reset, transitioning and thread-not-admitted are all decided here. Returns
	// Admitted only after IncrementActiveOperation has already run; the caller must ExitOperation on every later
	// failure path.
	private static LuaAdmissionStatus TryEnterAdmissionGate(bool bypassThreadAdmission,
		out LuaHostServices? services)
	{
		lock (SOperationGate)
		{
			services = s_services;
			if (services is null)
			{
				return LuaAdmissionStatus.Detached;
			}

			if (Read(ref s_externalResetDetected) != 0)
			{
				return LuaAdmissionStatus.ExternalStateReset;
			}

			if (!s_acceptOperations)
			{
				return LuaAdmissionStatus.TransitionInProgress;
			}

			if (!bypassThreadAdmission
				&& Read(ref s_threadAdmission) == (int) LuaThreadAdmission.MainThreadOnly
				&& Environment.CurrentManagedThreadId != services.MainThreadId
				&& t_operationDepth == 0)
			{
				return LuaAdmissionStatus.ThreadNotAdmitted;
			}

			IncrementActiveOperation();
			return LuaAdmissionStatus.Admitted;
		}
	}

	// The one-time write on the first admitted acquisition of an attachment (pitfall 6b); every later acquisition
	// only verifies. Interlocked so two racing first operations cannot both attempt the protected write.
	private static bool TryStampOrVerify(LuaState state)
	{
		if (Interlocked.CompareExchange(ref s_stamped, 1, 0) == 0)
		{
			state.PushLightUserdata(StampKeyForTests);
			state.RawSetPointer(LuaState.RegistryIndex, StampKeyForTests);
			return true;
		}

		return CheckStamp(state);
	}

	// Raw, non-allocating, no metamethod (pitfall 6c): a plain rawgetp/touserdata/pop. ZeroAllocationTests gates this.
	private static bool CheckStamp(LuaState state)
	{
		LuaType type = state.RawGetPointer(LuaState.RegistryIndex, StampKeyForTests);
		bool matches = type == LuaType.LightUserdata && state.ToUserdata(-1) == StampKeyForTests;
		state.Pop(1);
		return matches;
	}

	// Best-effort eager stamp at Attach (or after CompleteStateReset) time, on the state the binding already
	// provides. Deferred to the first admitted acquisition when the provider yields no state yet (pitfall 6a: tests
	// may attach with a provider returning null).
	private static void StampIfStateAvailable(LuaHostServices services)
	{
		lua_State* l = services.Provider();
		if (l is null)
		{
			return;
		}

		LuaState state = new(l);
		try
		{
			if (Interlocked.CompareExchange(ref s_stamped, 1, 0) == 0)
			{
				state.PushLightUserdata(StampKeyForTests);
				state.RawSetPointer(LuaState.RegistryIndex, StampKeyForTests);
			}
		}
		catch
		{
			// A stamp failure in Attach is an attach failure: revert to detached instead of publishing a binding
			// this SDK copy could never safely admit work on.
			Write(ref s_services, null);
			throw;
		}
	}

	// Re-checks the universe stamp on the provider state a lifecycle transition (Detach, a replacing Attach, or
	// BeginStateReset) would otherwise unref and neutralize against. Never mutates anything and never releases a
	// reference: a mismatch means the whole registry belongs to a different Lua universe (A08-22), so the caller
	// must abandon instead of unregistering into it.
	private static bool DetectExternalResetForDetach(LuaHostServices services, out LuaState state)
	{
		if (Read(ref s_externalResetDetected) != 0)
		{
			state = default;
			return true;
		}

		lua_State* l = services.Provider();
		state = new LuaState(l);
		if (l is null || Read(ref s_stamped) == 0 || CheckStamp(state))
		{
			return false;
		}

		ReportExternalReset();
		state = default;
		return true;
	}

	// First detector wins: advances the state generation (under LuaReferences.Gate, never held by a draining
	// transition), reverts to the conservative thread-admission default, and raises the one-shot diagnostic exactly
	// once, outside every runtime lock.
	private static void ReportExternalReset()
	{
		if (Interlocked.CompareExchange(ref s_externalResetDetected, 1, 0) != 0)
		{
			return;
		}

		lock (LuaReferences.Gate)
		{
			LuaStateIdentity identity = CurrentStateIdentity;
			PublishIdentity(identity.AttachEpoch, unchecked(identity.StateGeneration + 1));
		}

		Write(ref s_threadAdmission, (int) LuaThreadAdmission.MainThreadOnly);
		Read(ref DiagnosticObserver)?.Invoke(LuaRuntimeDiagnostic.ExternalStateReset);
	}

	private static void RaiseWorkerThreadRefusedOnce()
	{
		if (Interlocked.CompareExchange(ref s_workerThreadRefusedRaised, 1, 0) == 0)
		{
			Read(ref DiagnosticObserver)?.Invoke(LuaRuntimeDiagnostic.WorkerThreadRefused);
		}
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowForAdmissionStatus(LuaAdmissionStatus status)
	{
		switch (status)
		{
			case LuaAdmissionStatus.Detached:
				ThrowDetached();
				break;
			case LuaAdmissionStatus.TransitionInProgress:
				ThrowOperationAdmissionClosed();
				break;
			case LuaAdmissionStatus.ThreadNotAdmitted:
				ThrowThreadNotAdmitted();
				break;
			case LuaAdmissionStatus.ExternalStateReset:
				ThrowExternalStateReset();
				break;
			default:
				ThrowNoState();
				break;
		}
	}

	private static bool TryEnterOperation()
	{
		lock (SOperationGate)
		{
			if (s_services is null || !s_acceptOperations)
			{
				return false;
			}

			IncrementActiveOperation();
			return true;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void IncrementActiveOperation()
	{
		checked
		{
			s_activeOperations++;
		}

		t_operationDepth++;
		if (s_activeOperations == 1)
		{
			SOperationsDrained.Reset();
		}
	}

	private static bool IsOperationAdmissionOpen()
	{
		lock (SOperationGate)
		{
			return s_acceptOperations;
		}
	}

	private static void OpenOperationAdmission()
	{
		lock (SOperationGate)
		{
			if (s_services is not null)
			{
				s_acceptOperations = true;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void BeginTransition()
	{
		t_transitionDepth++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void EndTransition()
	{
		t_transitionDepth--;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ThrowIfResetTransitionActive()
	{
		if (s_resetTransitionActive)
		{
			throw new InvalidOperationException("A Lua state-reset transition is already active.");
		}
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowDetached()
	{
		throw new InvalidOperationException(
			"No host binding is attached: the plugin is not enabled, so there is no Lua state to talk to.");
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowNoState()
	{
		throw new InvalidOperationException("The host returned no Lua state for the calling thread.");
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowNoPusher()
	{
		throw new InvalidOperationException("The attached host binding has no host-object pusher.");
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowOperationAdmissionClosed()
	{
		throw new InvalidOperationException(
			"The Lua runtime is transitioning, so it is not accepting a new Lua operation.");
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowThreadNotAdmitted()
	{
		throw new InvalidOperationException(
			"The calling thread is not admitted for Lua work: the SDK's 2.0 conservative default (ADR-07) refuses a " +
			"worker thread before calling the host's state provider. Use CheatEngine.SDK.Hosting.Threading.MainThread.Invoke " +
			"to hop to the captured main thread, or opt in from OnEnable with the unqualified " +
			"LuaRuntime.AdmitWorkerThreads() [Experimental(\"CESDK5001\")].");
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowExternalStateReset()
	{
		throw new InvalidOperationException(
			"The host replaced its Lua state outside this SDK's controlled reset path (LuaStateReplacedExternally). " +
			"Every owner from before the replacement is refused; disable and re-enable the plugin to recover.");
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowTransitionFromOperation()
	{
		throw new InvalidOperationException(
			"A Lua lifecycle transition cannot start from an admitted Lua operation because it would wait for itself.");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ThrowIfTransitionFromCurrentOperation()
	{
		if (t_operationDepth != 0)
		{
			ThrowTransitionFromOperation();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void PublishIdentity(int attachEpoch, int stateGeneration)
	{
		Write(ref s_identity, PackIdentity(attachEpoch, stateGeneration));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long PackIdentity(int attachEpoch, int stateGeneration)
	{
		return ((long) attachEpoch << 32) | (uint) stateGeneration;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static LuaStateIdentity UnpackIdentity(long packed)
	{
		return new LuaStateIdentity((int) (packed >> 32), (int) (uint) packed);
	}

	/// <summary>Result of an internal callback-disposal operation acquisition.</summary>
	internal enum LuaCallbackDisposeOperationResult
	{
		Acquired,
		Unavailable,
		AdmissionClosed,

		/// <summary>The calling thread is not admitted under the current <see cref="LuaThreadAdmission" /> policy.</summary>
		ThreadNotAdmitted,

		/// <summary>An external Lua-state reset was detected; every owner from before it is refused.</summary>
		ExternalStateReset
	}
}
