using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Objects;

/// <summary>
///     Ownership of one Cheat Engine object that this plugin created: the wrapper that destroys it, explicitly, on
///     <see cref="Dispose" />. The handle it wraps stays a plain <typeparamref name="T" />; ownership is the wrapper,
///     not a flag on the handle.
/// </summary>
/// <typeparam name="T">The handle type: <see cref="CEObject" /> for an untyped object, or a typed wrapper struct.</typeparam>
/// <remarks>
///     <para>
///         <b>Why a generic wrapper and not a class hierarchy.</b> The same Cheat Engine class comes in both ownerships:
///         <c>createMemScan()</c> returns a scanner the plugin owns, <c>getCurrentMemscan()</c> returns the GUI's, which
///         it
///         must never destroy. With one struct per class as the borrowed handle and this wrapper as the owned form,
///         <c>MemScan</c> and <c>Owned&lt;MemScan&gt;</c> are two types with the same API underneath and no <c>Dispose</c>
///         on the borrowed one, so the compiler, not a runtime flag, keeps a borrowed object from being destroyed. A class
///         hierarchy would need either a mutable "suppress destroy" flag or two parallel hierarchies, and would
///         allocate for every borrowed object. The price is one indirection: the typed members live on
///         <see cref="Value" />.
///     </para>
///     <para>
///         <b>Construction and transfer.</b> The constructor is internal: only a sourced SDK factory which received a
///         documented caller-owned CE result can create an owner. A consumer cannot convert an arbitrary borrowed handle
///         into a destructible object. <see cref="Transfer" /> is the only public move operation: it returns a new owner
///         and leaves this one empty; the new owner keeps this owner's <see cref="Origin" />. <see cref="Abandon" /> is
///         deliberately different: it stops managed cleanup and returns a <em>borrowed</em> handle; it does not grant
///         another caller permission to construct an owner.
///     </para>
///     <para>
///         <b>Origin.</b> The owner captures the Lua runtime identity (attach epoch and state generation) current when
///         its factory created the object, inside that factory's admitted Lua operation. An object created in one Lua
///         universe is never destroyed in another: after a re-enable or a controlled Lua state replacement the owner is
///         <em>consumed without any Cheat Engine call</em> and <see cref="LastReleaseOutcome" /> reports
///         <see cref="TargetReleaseStatus.RefusedRuntimeChanged" />. The native object may then still exist (a residue
///         to recover manually); destroying a handle through another universe could free an unrelated object.
///     </para>
///     <para>
///         <b>Lifetime.</b> <see cref="Dispose" /> calls the object's <c>destroy()</c> through a protected call while
///         the plugin binding and its host-object pusher remain available. There is no finalizer (an object must never
///         be destroyed from the finalizer thread, and a native object must never be freed behind Cheat Engine's back at
///         an arbitrary time), so an undisposed wrapper leaks the object until Cheat Engine exits. Dispose twice is
///         harmless. <see cref="ReleaseWithOutcome" /> is the never-throwing form that always consumes the owner and
///         reports what happened.
///     </para>
///     <para>
///         <b>Failure modes.</b> When the destroy call raises (the object is already gone, for example destroyed by its
///         parent, or its class refuses), the wrapper is still marked empty: a destroy is never retried, because the
///         object may be half freed. <see cref="TryDestroy" /> returns that status with the message on the stack;
///         <see cref="Dispose" /> intentionally discards it after an invocation began, and
///         <see cref="LastReleaseOutcome" />
///         records it as unconfirmed. By contrast, a detached runtime, a missing pusher, or a state that is unavailable on
///         the calling thread prevents an invocation from beginning. Those cases make <see cref="Dispose" /> and
///         <see cref="TryDestroy" /> throw and retain this owner, so the plugin can retry before disable finishes or
///         explicitly <see cref="Abandon" /> it (a later re-enable makes the retained owner stale, so the next attempt
///         consumes it without a Cheat Engine call). They never masquerade as successful destruction.
///     </para>
///     <para>
///         <b>Threads.</b> One owner, one thread at a time; the type is not synchronized. Reading <see cref="Value" />
///         from
///         another thread while the owner disposes is a race the caller has to prevent. Cheat Engine 7.7 documentation
///         does not establish one universal thread affinity for every class's <c>destroy()</c>, so this generic owner
///         does not infer one. A typed factory or wrapper with evidence for an affinity must enforce and annotate that
///         narrower contract itself.
///     </para>
/// </remarks>
public sealed class Owned<T> : IDisposable
	where T : struct, ICEObject<T>
{
	private T _value;

	/// <summary>
	///     Takes ownership of <paramref name="value" /> from an SDK factory with an established ownership contract,
	///     capturing the current Lua runtime identity as its origin.
	/// </summary>
	/// <param name="value">
	///     A handle to an object nobody else owns, obtained from a documented SDK creation binding or from
	///     <see cref="Transfer" />.
	/// </param>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a null handle.</exception>
	/// <remarks>Call it inside the admitted Lua operation that created the object, so the captured identity is stable.</remarks>
	internal Owned(T value)
		: this(value, LuaRuntime.CurrentStateIdentity)
	{
	}

	/// <summary>Takes ownership of <paramref name="value" /> with an explicit origin, for transfers.</summary>
	/// <param name="value">The handle to own.</param>
	/// <param name="origin">The Lua runtime identity that created the object.</param>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a null handle.</exception>
	internal Owned(T value, LuaStateIdentity origin)
	{
		if (value.Handle.IsNull)
		{
			throw new ArgumentException("A null handle cannot be owned.", nameof(value));
		}

		_value = value;
		Origin = new EngineResourceOrigin(origin, null);
	}

	/// <summary>
	///     Gets the typed handle, for calling the object's members. A borrowed view: do not keep it beyond the wrapper's
	///     life, and check <see cref="EngineResourceOrigin.IsCurrentRuntime" /> of <see cref="Origin" /> before using a
	///     handle kept across a re-enable.
	/// </summary>
	/// <exception cref="ObjectDisposedException">The wrapper was disposed, transferred or abandoned.</exception>
	public T Value
	{
		get
		{
			if (IsDisposed)
			{
				ThrowDisposed();
			}

			return _value;
		}
	}

	/// <summary>Gets the untyped handle of the owned object.</summary>
	/// <exception cref="ObjectDisposedException">The wrapper was disposed, transferred or abandoned.</exception>
	public CEObject Handle => Value.Handle;

	/// <summary>
	///     Gets a value indicating whether the wrapper no longer owns anything, after <see cref="Dispose" />,
	///     <see cref="TryDestroy" />, <see cref="ReleaseWithOutcome" />, <see cref="Transfer" /> or <see cref="Abandon" />.
	/// </summary>
	public bool IsDisposed => _value.Handle.IsNull;

	/// <summary>
	///     Gets the Lua runtime identity that created the object. The owner has no target component: a plugin-owned Cheat
	///     Engine object is not bound to a target process incarnation.
	/// </summary>
	/// <remarks>Still readable after the owner was consumed, for diagnostics.</remarks>
	public EngineResourceOrigin Origin
	{
		get;
	}

	/// <summary>
	///     Gets the factual outcome of the release attempt that consumed this owner:
	///     <see cref="TargetReleaseStatus.Released" /> after a confirmed <c>destroy()</c>,
	///     <see cref="TargetReleaseStatus.UnconfirmedAfterInvocation" /> after a <c>destroy()</c> that raised,
	///     <see cref="TargetReleaseStatus.RefusedRuntimeChanged" /> when the origin runtime was no longer current, or
	///     <see cref="TargetReleaseStatus.NotInvoked" /> when <see cref="ReleaseWithOutcome" /> could not begin a call.
	///     <see cref="TargetReleaseStatus.Unspecified" /> while the owner is live, and after a transfer or an abandonment.
	/// </summary>
	public TargetReleaseOutcome LastReleaseOutcome
	{
		get;
		private set;
	}

	/// <summary>
	///     Destroys the object through <see cref="TryDestroy" /> on the ambient state, discarding a protected Lua
	///     failure after an invocation began. It is idempotent. If the origin runtime identity is no longer current, the
	///     owner is consumed without any Cheat Engine call and the refusal is recorded. If the runtime is detached, the
	///     current thread cannot obtain a state, or the binding has no host-object pusher, it throws and retains ownership
	///     so the caller can retry or explicitly abandon the object; it never reports a no-op as a completed destruction.
	/// </summary>
	public void Dispose()
	{
		if (IsDisposed)
		{
			return;
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		if (!IsOriginCurrent())
		{
			ConsumeWithoutDestroy(TargetReleaseOutcome.RefusedRuntimeChanged());
			return;
		}

		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		_ = TryDestroyCore(state);
	}

	/// <summary>
	///     Releases the object and reports the outcome. Never throws and always consumes this owner, on every path: the
	///     one permitted <c>destroy()</c> is never retried.
	/// </summary>
	/// <returns>
	///     <see cref="LastReleaseOutcome" /> when the owner was already consumed;
	///     <see cref="TargetReleaseStatus.RefusedRuntimeChanged" /> when the origin runtime is no longer current (no
	///     Cheat Engine call); <see cref="TargetReleaseStatus.NotInvoked" /> with
	///     <see cref="EngineFailureKind.BindingFailure" /> when the runtime is detached, admission is closed, the thread
	///     has no Lua state or the binding has no host-object pusher (no call; the object leaks until Cheat Engine exits);
	///     <see cref="TargetReleaseStatus.Released" /> after a confirmed <c>destroy()</c>; or
	///     <see cref="TargetReleaseStatus.UnconfirmedAfterInvocation" /> with
	///     <see cref="EngineFailureKind.ProtectedLuaFailure" /> when <c>destroy()</c> raised.
	/// </returns>
	/// <remarks>
	///     Use it where cleanup must not throw and must not be retried: a detached runtime consumes the owner here,
	///     whereas <see cref="Dispose" /> retains it. The Lua stack is restored on every path.
	/// </remarks>
	public TargetReleaseOutcome ReleaseWithOutcome()
	{
		if (IsDisposed)
		{
			return LastReleaseOutcome;
		}

		if (!IsOriginCurrent())
		{
			return ConsumeWithoutDestroy(TargetReleaseOutcome.RefusedRuntimeChanged());
		}

		if (!LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation))
		{
			return ConsumeWithoutDestroy(TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure));
		}

		using (operation)
		{
			// The identity is stable while an operation is admitted: a transition closes admission and drains first.
			if (!IsOriginCurrent())
			{
				return ConsumeWithoutDestroy(TargetReleaseOutcome.RefusedRuntimeChanged());
			}

			LuaState state = operation.State;
			if (!CanStartDestruction(state))
			{
				return ConsumeWithoutDestroy(TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure));
			}

			using LuaFrame frame = new(state);
			LuaStatus status;
			try
			{
				status = _value.Handle.TryDestroy(state);
			}
			catch (Exception)
			{
				// Only the host-object push can throw, before destroy() is called: nothing was invoked.
				return ConsumeWithoutDestroy(TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure));
			}

			return Consume(status);
		}
	}

	/// <summary>
	///     The handle as a borrowed value, for passing the object to an API that does not take ownership. Same as
	///     <see cref="Value" />, named for the intent.
	/// </summary>
	/// <returns>The typed handle.</returns>
	/// <exception cref="ObjectDisposedException">The wrapper was disposed, transferred or abandoned.</exception>
	public T ToBorrowed()
	{
		return Value;
	}

	/// <summary>
	///     Moves this ownership capability into a new wrapper without calling CE. The source wrapper becomes empty; the
	///     new wrapper keeps the source's <see cref="Origin" />.
	/// </summary>
	/// <returns>The new sole owner.</returns>
	/// <exception cref="ObjectDisposedException">The wrapper was disposed, transferred or abandoned.</exception>
	public Owned<T> Transfer()
	{
		Owned<T> destination = PrepareTransfer();
		CompleteTransfer(destination);
		return destination;
	}

	// A multi-owner handoff prepares every destination before any source is made empty. These members are internal so
	// that a consumer cannot ever observe the brief, private preparation state as a second ownership capability. The
	// destination keeps the source's origin: a transfer never re-stamps an owner with the current runtime identity.
	internal Owned<T> PrepareTransfer()
	{
		T value = Value;
		return new Owned<T>(value, Origin.Runtime);
	}

	internal void CompleteTransfer(Owned<T> destination)
	{
		ArgumentNullException.ThrowIfNull(destination);
		if (destination.Value.Handle != Value.Handle)
		{
			throw new ArgumentException("The destination does not represent this owned object.", nameof(destination));
		}

		_value = default;
	}

	/// <summary>
	///     Explicitly stops managed cleanup without calling CE and returns a borrowed handle. This is abandonment, not
	///     an ownership transfer: the returned value cannot be wrapped in <see cref="Owned{T}" /> by consumer code.
	/// </summary>
	/// <returns>The still-live object as a borrowed handle.</returns>
	/// <exception cref="ObjectDisposedException">The wrapper was already empty.</exception>
	/// <remarks>
	///     Use only when ownership has moved into a CE operation whose contract is already documented, or when an
	///     unavoidable shutdown path has been recorded. Prefer <see cref="Transfer" /> for a managed hand-off.
	/// </remarks>
	public T Abandon()
	{
		T value = Value;
		_value = default;
		return value;
	}

	/// <summary>
	///     Destroys the object now, through a protected <c>destroy()</c> call on <paramref name="state" />, and marks the
	///     wrapper empty after a protected call began, whatever its status. Stack after success: unchanged; after
	///     failure: one error value, for the caller's frame to read or discard. Already empty: returns
	///     <see cref="LuaStatus.Ok" /> and pushes nothing.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <returns>The status of the destroy call.</returns>
	/// <exception cref="InvalidOperationException">
	///     The plugin is not enabled, the caller has no Lua state, the supplied state is not the calling thread's, or the
	///     attached host binding has no object pusher (an embedding without <c>LuaPushClassInstance</c>): nothing was
	///     called, the stack is untouched, and this wrapper retains ownership for an explicit retry or
	///     <see cref="Abandon" />. Also thrown, after the owner was <em>consumed</em> without any call, when the owner
	///     belongs to a previous Lua runtime identity (<see cref="LastReleaseOutcome" /> then reports
	///     <see cref="TargetReleaseStatus.RefusedRuntimeChanged" />).
	/// </exception>
	[RequiresPluginEnabled]
	public LuaStatus TryDestroy(LuaState state)
	{
		if (IsDisposed)
		{
			return LuaStatus.Ok;
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		if (operation.State != state)
		{
			throw new InvalidOperationException(
				"The supplied Lua state is not the state currently assigned to this thread by the attached host.");
		}

		if (!IsOriginCurrent())
		{
			ConsumeWithoutDestroy(TargetReleaseOutcome.RefusedRuntimeChanged());
			throw new InvalidOperationException(
				"The owned Cheat Engine object belongs to a previous Lua runtime identity; ownership was consumed without destroy.");
		}

		return TryDestroyCore(operation.State);
	}

	private LuaStatus TryDestroyCore(LuaState state)
	{
		EnsureDestructionCanStart(state);
		LuaStatus status = _value.Handle.TryDestroy(state);
		_ = Consume(status);
		return status;
	}

	/// <summary><c>Owned(CEObject@0x...)</c>, or <c>Owned(disposed)</c>.</summary>
	public override string ToString()
	{
		return IsDisposed ? "Owned(disposed)" : "Owned(" + _value.Handle + ")";
	}

	private bool IsOriginCurrent()
	{
		return EngineResourceOrigin.IsCurrent(Origin.Runtime);
	}

	// A destroy() call was made: the owner is consumed whatever its status and never retried.
	private TargetReleaseOutcome Consume(LuaStatus destroyStatus)
	{
		_value = default;
		LastReleaseOutcome = destroyStatus.IsOk
			? TargetReleaseOutcome.Released()
			: TargetReleaseOutcome.Unconfirmed(EngineFailureKind.ProtectedLuaFailure);
		return LastReleaseOutcome;
	}

	private TargetReleaseOutcome ConsumeWithoutDestroy(TargetReleaseOutcome outcome)
	{
		_value = default;
		LastReleaseOutcome = outcome;
		return outcome;
	}

	private static bool CanStartDestruction(LuaState state)
	{
		LuaHostBinding binding = LuaRuntime.CurrentBinding;
		return binding.IsValid && binding.HostObjectPusher != 0 && !state.IsNull;
	}

	private static void EnsureDestructionCanStart(LuaState state)
	{
		LuaHostBinding binding = LuaRuntime.CurrentBinding;
		if (!binding.IsValid)
		{
			throw new InvalidOperationException(
				"The plugin is not enabled, so the owned Cheat Engine object cannot be destroyed yet.");
		}

		if (binding.HostObjectPusher == 0)
		{
			throw new InvalidOperationException(
				"The attached host binding has no host-object pusher, so the owned Cheat Engine object cannot be destroyed.");
		}

		if (state.IsNull)
		{
			throw new InvalidOperationException(
				"The current thread has no Lua state, so the owned Cheat Engine object cannot be destroyed.");
		}
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowDisposed()
	{
		throw new ObjectDisposedException(typeof(Owned<T>).Name,
			"The wrapper no longer owns an object: it was disposed, transferred or abandoned.");
	}
}
