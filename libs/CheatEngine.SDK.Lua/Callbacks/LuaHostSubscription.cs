using System;
using System.Collections.Generic;
using System.Threading;

using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Callbacks;

/// <summary>
///     An SDK-internal owner for a host callback registration whose host object must be released while its Lua state is
///     still reachable.
/// </summary>
/// <remarks>
///     This is deliberately not a timer or hotkey API. The managed CE export contract does not contain those operations,
///     and their Lua paths have not received a controlled live-host qualification. A future, qualified mapper supplies a
///     registrar that returns an unregister action only after it has completely created the host subscription. The owner
///     then gives that action one state-bound, LIFO teardown attempt during reset, disable, or replacement.
/// </remarks>
internal sealed class LuaHostSubscription : IDisposable
{
	[ThreadStatic] private static List<LuaHostSubscription>? t_dispatchStack;

	// Deterministic drain seam used only by the SDK's friend test assembly. It runs in the individual owner drain and
	// therefore exposes whether a registry caller kept its gate while asking that owner to wait for a callback.
	internal static Action? CallbackDrainStartedForTesting;
	private readonly ManualResetEventSlim _callbacksDrained = new(true);
	private readonly ManualResetEventSlim _disposed = new(false);

	private readonly Lock _gate = new();
	private bool _acceptCallbacks;
	private int _activeCallbacks;

	private Action? _callback;
	private int _disposeStarted;
	private bool _isDisposed;
	private Action<LuaState>? _unregister;

	private LuaHostSubscription(Action callback, LuaStateIdentity identity)
	{
		_callback = callback;
		Identity = identity;
	}

	/// <summary>Gets the attachment and state generation that owns this registration.</summary>
	internal LuaStateIdentity Identity
	{
		get;
	}

	/// <summary>Gets whether teardown has consumed this host registration.</summary>
	internal bool IsDisposed => Volatile.Read(ref _isDisposed);

	/// <summary>Gets the last managed handler failure, which is contained instead of reaching the host callback.</summary>
	internal Exception? LastCallbackException
	{
		get;
		private set;
	}

	/// <summary>Gets the last unregister failure; ownership remains consumed and the action is never retried.</summary>
	internal Exception? LastUnregisterException
	{
		get;
		private set;
	}

	internal LuaHostSubscription? Next
	{
		get;
		set;
	}

	internal LuaHostSubscription? Previous
	{
		get;
		set;
	}

	internal bool IsLinked
	{
		get;
		set;
	}

	/// <summary>
	///     Releases this owner with a state borrowed from the active attachment. If a lifecycle transition has already
	///     closed operation admission, its registry entry remains owned by that transition instead of attempting a raw
	///     state access after teardown started.
	/// </summary>
	public void Dispose()
	{
		if (IsDispatchingOnCurrentThread())
		{
			throw new InvalidOperationException(
				"A host subscription cannot be disposed from its own callback; request teardown after the callback returns.");
		}

		LuaRuntime.LuaCallbackDisposeOperationResult result =
			LuaRuntime.TryAcquireOperationForCallbackDispose(out LuaRuntimeOperation operation);
		if (result == LuaRuntime.LuaCallbackDisposeOperationResult.Acquired)
		{
			using (operation)
			{
				ReleaseWithState(operation.State);
			}

			return;
		}

		if (result is LuaRuntime.LuaCallbackDisposeOperationResult.AdmissionClosed
			or LuaRuntime.LuaCallbackDisposeOperationResult.ThreadNotAdmitted
			or LuaRuntime.LuaCallbackDisposeOperationResult.ExternalStateReset)
		{
			// The lifecycle transition still owns the linked registration and has the only state allowed to unregister
			// it. In particular, do not consume the action here: a failed transition can reopen the old binding, and a
			// detected external reset must never unregister into the replacement registry (A08-22).
			return;
		}

		AbandonWithoutState();
	}

	/// <summary>
	///     Creates and publishes one owner transactionally. The registrar receives an inert callback and must return an
	///     unregister action only after the host has accepted the registration. A <see langword="null" /> return or a
	///     thrown registrar must mean that no host subscription requiring cleanup was created.
	/// </summary>
	internal static bool TryRegister(LuaState state, Action callback,
		Func<LuaState, Action, Action<LuaState>?> registrar, out LuaHostSubscription? subscription)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(registrar);

		subscription = null;
		if (state.IsNull || !LuaRuntime.IsAttached)
		{
			return false;
		}

		using LuaRuntimeOperation operation = LuaRuntime.EnterStateOperation(state);
		if (!LuaRuntime.IsAttached)
		{
			return false;
		}

		LuaHostSubscription candidate = new(callback, LuaRuntime.CurrentStateIdentity);
		Action<LuaState>? unregister;
		try
		{
			unregister = registrar(state, candidate.Dispatch);
		}
		catch
		{
			candidate.CancelUnregisteredRegistration();
			throw;
		}

		if (unregister is null)
		{
			candidate.CancelUnregisteredRegistration();
			return false;
		}

		candidate._unregister = unregister;
		if (!LuaHostSubscriptionRegistry.TryAdd(candidate))
		{
			candidate.ReleaseUnpublishedRegistration(state);
			return false;
		}

		subscription = candidate;
		return true;
	}

	/// <summary>Stops new handler entry and waits for admitted handlers to leave without unregistering the host object.</summary>
	internal void CloseCallbackAdmissionAndDrain()
	{
		lock (_gate)
		{
			_acceptCallbacks = false;
		}

		Volatile.Read(ref CallbackDrainStartedForTesting)?.Invoke();
		_callbacksDrained.Wait();
	}

	/// <summary>Called only by the lifecycle registry while the old state is still valid.</summary>
	internal void ReleaseFromLifecycle(LuaState state)
	{
		ReleaseWithState(state);
	}

	/// <summary>
	///     Called only by <see cref="LuaHostSubscriptionRegistry.AbandonAll" /> after an external reset was detected:
	///     marks this owner released and keeps its managed state alive, without calling the host's unregister action.
	///     Unregistering it would touch the replacement Lua universe's registry, which this SDK copy never created
	///     (A08-22); leaking the managed state instead is the safe failure.
	/// </summary>
	internal void Abandon()
	{
		AbandonWithoutState();
	}

	private void Dispatch()
	{
		if (!LuaRuntime.TryEnterCallbackOperation(out LuaRuntimeOperation operation))
		{
			return;
		}

		using (operation)
		{
			if (!TryEnterCallback(out Action callback))
			{
				return;
			}

			List<LuaHostSubscription> stack = t_dispatchStack ??= [];
			stack.Add(this);
			try
			{
				callback();
			}
			catch (Exception exception)
			{
				LastCallbackException = exception;
			}
			finally
			{
				stack.RemoveAt(stack.Count - 1);
				ExitCallback();
			}
		}
	}

	private bool TryEnterCallback(out Action callback)
	{
		lock (_gate)
		{
			callback = null!;
			if (!_acceptCallbacks
				|| !LuaRuntime.IsAttached
				|| LuaRuntime.CurrentStateIdentity != Identity
				|| _callback is null)
			{
				return false;
			}

			checked
			{
				_activeCallbacks++;
			}

			_callbacksDrained.Reset();
			callback = _callback;
			return true;
		}
	}

	private void ExitCallback()
	{
		lock (_gate)
		{
			if (--_activeCallbacks == 0)
			{
				_callbacksDrained.Set();
			}
		}
	}

	private bool IsDispatchingOnCurrentThread()
	{
		List<LuaHostSubscription>? stack = t_dispatchStack;
		if (stack is null)
		{
			return false;
		}

		for (int index = stack.Count - 1; index >= 0; index--)
		{
			if (ReferenceEquals(stack[index], this))
			{
				return true;
			}
		}

		return false;
	}

	private void ReleaseWithState(LuaState state)
	{
		if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0)
		{
			_disposed.Wait();
			return;
		}

		try
		{
			CloseCallbackAdmissionAndDrain();
			Action<LuaState>? unregister;
			lock (_gate)
			{
				_callback = null;
				unregister = _unregister;
				_unregister = null;
			}

			if (unregister is not null)
			{
				try
				{
					unregister(state);
				}
				catch (Exception exception)
				{
					LastUnregisterException = exception;
				}
			}
		}
		finally
		{
			LuaHostSubscriptionRegistry.Remove(this);
			Volatile.Write(ref _isDisposed, true);
			_disposed.Set();
		}
	}

	private void ReleaseUnpublishedRegistration(LuaState state)
	{
		ReleaseWithState(state);
	}

	private void CancelUnregisteredRegistration()
	{
		lock (_gate)
		{
			_acceptCallbacks = false;
			_callback = null;
		}

		Volatile.Write(ref _isDisposed, true);
		_disposed.Set();
	}

	private void AbandonWithoutState()
	{
		if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0)
		{
			_disposed.Wait();
			return;
		}

		try
		{
			CloseCallbackAdmissionAndDrain();
			lock (_gate)
			{
				_callback = null;
				_unregister = null;
			}
		}
		finally
		{
			LuaHostSubscriptionRegistry.Remove(this);
			Volatile.Write(ref _isDisposed, true);
			_disposed.Set();
		}
	}

	internal void Publish()
	{
		lock (_gate)
		{
			_acceptCallbacks = true;
		}
	}
}
