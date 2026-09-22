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
///     The ambient binding between this SDK copy and its host: one per assembly load context (one per plugin), owned by
///     this assembly so that <c>CheatEngine.SDK.Engine</c> and generated code can reach the host's Lua state without
///     referencing <c>CheatEngine.SDK.Hosting</c> or the ABI.
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
///         <b>State replacement.</b> A reset must first call the SDK-controlled preparation path while the old state is
///         reachable. That path neutralizes rooted callbacks and invalidates state-bound resources before the host
///         replaces the state. Calling Cheat Engine's <c>resetLuaState</c> outside that path is unsupported: this SDK
///         intentionally does not guess whether an old registry slot or callback closure remains valid.
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

	// A transition owner is allowed to release callbacks and references after admission has closed. An active Lua
	// operation is never allowed to start a transition: doing so would wait for itself and deadlock.
	[ThreadStatic] private static int t_operationDepth;

	[ThreadStatic] private static int t_transitionDepth;

	// Deterministic lifecycle-race seam used only by the SDK's friend test assembly. It is invoked after admission is
	// closed and before the drain wait, outside every runtime lock.
	internal static Action? OperationAdmissionClosedForTesting;

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
		if (TryEnterProviderOperation(out LuaState state) == LuaCallbackDisposeOperationResult.Acquired)
		{
			return new LuaRuntimeOperation(state, true);
		}

		if (Read(ref s_services) is null)
		{
			ThrowDetached();
		}

		if (!IsOperationAdmissionOpen())
		{
			ThrowOperationAdmissionClosed();
		}

		throw new InvalidOperationException("The host returned no Lua state for the calling thread.");
	}

	/// <summary>Non-throwing <see cref="AcquireOperation()" />.</summary>
	/// <param name="operation">The admitted operation on success; default otherwise.</param>
	/// <returns>
	///     <see langword="true" /> when a binding is attached, admission is open and the host provided a state for the
	///     calling thread.
	/// </returns>
	[RequiresPluginEnabled]
	public static bool TryAcquireOperation(out LuaRuntimeOperation operation)
	{
		if (TryEnterProviderOperation(out LuaState state) == LuaCallbackDisposeOperationResult.Acquired)
		{
			operation = new LuaRuntimeOperation(state, true);
			return true;
		}

		operation = default;
		return false;
	}

	/// <summary>
	///     Acquires an operation for <see cref="LuaCallback.Dispose()" /> and reports whether an attached lifecycle
	///     transition, rather than an unavailable state, rejected it.
	/// </summary>
	/// <remarks>
	///     The result is selected while <c>SOperationGate</c> is held. In particular, an
	///     <see cref="LuaCallbackDisposeOperationResult.AdmissionClosed" /> result cannot be reinterpreted as detached
	///     after an unsuccessful transition reopens admission: callback disposal must leave registry ownership with that
	///     transition until a state has neutralized the Lua closure.
	/// </remarks>
	internal static LuaCallbackDisposeOperationResult TryAcquireOperationForCallbackDispose(
		out LuaRuntimeOperation operation)
	{
		LuaCallbackDisposeOperationResult result = TryEnterProviderOperation(out LuaState state);
		operation = result == LuaCallbackDisposeOperationResult.Acquired
			? new LuaRuntimeOperation(state, true)
			: default;
		return result;
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
				LuaHostServices? previous = s_services;
				if (previous is not null)
				{
					LuaHostSubscriptionRegistry.DetachAll(new LuaState(previous.Provider()));
					LuaCallbackRegistry.DetachAll(previous);
				}

				lock (LuaReferences.Gate)
				{
					LuaStateIdentity identity = CurrentStateIdentity;
					PublishIdentity(unchecked(identity.AttachEpoch + 1), identity.StateGeneration);
					Write(ref s_services, new LuaHostServices(binding));
					LuaHostSubscriptionRegistry.OpenRegistrationAdmission();
				}
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
				LuaHostSubscriptionRegistry.DetachAll(new LuaState(services.Provider()));
				LuaCallbackRegistry.DetachAll(services);
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
				LuaHostSubscriptionRegistry.DetachAll(new LuaState(services.Provider()));
				LuaCallbackRegistry.DetachAll(services);
				Write(ref s_services, null);
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
			if (Read(ref s_services) is not null)
			{
				LuaHostSubscriptionRegistry.OpenRegistrationAdmission();
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

	private static LuaCallbackDisposeOperationResult TryEnterProviderOperation(out LuaState state)
	{
		LuaHostServices? services;
		lock (SOperationGate)
		{
			services = s_services;
			if (services is null)
			{
				state = default;
				return LuaCallbackDisposeOperationResult.Unavailable;
			}

			if (!s_acceptOperations)
			{
				state = default;
				return LuaCallbackDisposeOperationResult.AdmissionClosed;
			}

			IncrementActiveOperation();
		}

		try
		{
			lua_State* l = services.Provider();
			if (l is null)
			{
				ExitOperation();
				state = default;
				return LuaCallbackDisposeOperationResult.Unavailable;
			}

			state = new LuaState(l);
			return LuaCallbackDisposeOperationResult.Acquired;
		}
		catch
		{
			ExitOperation();
			throw;
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
		AdmissionClosed
	}
}
