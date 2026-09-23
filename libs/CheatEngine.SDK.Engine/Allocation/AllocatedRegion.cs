using System;
using System.Threading;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The sole owner of one target-process allocation returned by <see cref="TargetMemoryAllocator" />.
/// </summary>
/// <remarks>
///     <para>
///         The region is not a managed allocation and has no finalizer: <c>deAlloc</c> must execute while the plugin
///         remains enabled. CE 7.7's catalog establishes no GUI-thread affinity for this global, so this type
///         intentionally carries no <c>MainThreadOnly</c> assertion until a live probe provides that evidence. Call
///         <see cref="Dispose" /> in a <see langword="using" /> block for best-effort, no-throw cleanup,
///         <see cref="ReleaseWithTargetOutcome" /> for a no-throw structured result, or <see cref="Release" /> when the
///         caller must observe a failure as an exception. Every path consumes ownership before invoking CE; an expected
///         failure, a binding/marshalling failure, or a Lua exception never causes a retry. This makes concurrent and
///         repeated cleanup deterministic and prevents a stale address from being freed twice.
///     </para>
///     <para>
///         <b>Origin.</b> The region is bound to the Lua runtime identity and the target process incarnation that
///         created it (<see cref="Origin" />). After a re-enable or a controlled Lua state replacement it refuses cleanup
///         without any Cheat Engine call and reports <see cref="TargetReleaseStatus.RefusedRuntimeChanged" />: the
///         allocation may remain in the target as a residue. After a detach, cleanup cannot begin and reports
///         <see cref="TargetReleaseStatus.NotInvoked" />. Before invoking CE, the owner reads the current selection and
///         refuses when it is not the captured process incarnation; it never reopens or selects a process for cleanup.
///         CE exposes no primitive that makes that observation atomic with a following ambient-target Lua call, so a
///         selection change in that external interval remains unqualified rather than being represented as a stronger
///         guarantee. A later allocation that reuses the same address is never freed through this owner: after its one
///         release attempt the owner is consumed.
///     </para>
/// </remarks>
public sealed class AllocatedRegion : IDisposable
{
	private readonly Address _address;
	private readonly TargetAllocationSize _size;
	private readonly ITargetBoundMemoryAllocationOperations _targetBoundOperations;
	private TargetReleaseOutcome _lastReleaseOutcome;
	private int _released;

	internal AllocatedRegion(ITargetBoundMemoryAllocationOperations targetBoundOperations, Address address,
		TargetAllocationSize size, TargetProcessIncarnation targetIncarnation, LuaStateIdentity runtime)
	{
		ArgumentNullException.ThrowIfNull(targetBoundOperations);
		if (address.IsZero)
		{
			throw new ArgumentException("An allocated region needs a nonzero target address.", nameof(address));
		}

		if (size.Value <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size), size.Value,
				"An allocated region needs a positive allocation size.");
		}

		_targetBoundOperations = targetBoundOperations;
		TargetIncarnation = targetIncarnation;
		Origin = new EngineResourceOrigin(runtime, targetIncarnation);
		_address = address;
		_size = size;
	}

	/// <summary>
	///     Gets the owned address in the attached target process.
	/// </summary>
	/// <exception cref="ObjectDisposedException">Ownership was released or disposed.</exception>
	public Address Address
	{
		get
		{
			ThrowIfReleased();
			return _address;
		}
	}

	/// <summary>
	///     Gets the positive byte count originally passed to <c>allocateMemory</c>.
	/// </summary>
	/// <exception cref="ObjectDisposedException">Ownership was released or disposed.</exception>
	public TargetAllocationSize Size
	{
		get
		{
			ThrowIfReleased();
			return _size;
		}
	}

	/// <summary>
	///     Gets a value indicating whether ownership has been consumed by <see cref="Release" />,
	///     <see cref="ReleaseWithOutcome" />, <see cref="ReleaseWithTargetOutcome" />, or <see cref="Dispose" />.
	/// </summary>
	public bool IsDisposed => Volatile.Read(ref _released) != 0;

	/// <summary>Gets the copied process incarnation that was qualified when this allocation was created.</summary>
	/// <remarks>The same value as the <see cref="EngineResourceOrigin.Target" /> of <see cref="Origin" />.</remarks>
	public TargetProcessIncarnation TargetIncarnation
	{
		get;
	}

	/// <summary>
	///     Gets the Lua runtime identity and the target process incarnation that created this allocation. Readable after
	///     the owner was consumed, for diagnostics.
	/// </summary>
	public EngineResourceOrigin Origin
	{
		get;
	}

	/// <summary>
	///     Gets the factual outcome of the one release attempt: confirmed release, a safe target or runtime refusal,
	///     cleanup that could not begin, or an attempted but unconfirmed deallocation.
	/// </summary>
	public TargetReleaseOutcome LastReleaseOutcome => _lastReleaseOutcome;

	/// <summary>
	///     Best-effort no-throw release of the target allocation. Idempotent, including concurrent calls.
	/// </summary>
	/// <remarks>
	///     This method is intended for <see langword="using" />/<see langword="finally" /> cleanup. It intentionally discards
	///     expected CE, Lua, binding, marshalling and lifecycle failures, but still consumes ownership so a later call never
	///     retries a possibly partial deallocation. <see cref="LastReleaseOutcome" /> records what happened, including a
	///     refusal after a re-enable and <see cref="TargetReleaseStatus.NotInvoked" /> after a detach. Call
	///     <see cref="Release" /> when the failure must be observed as an exception.
	/// </remarks>
	[RequiresPluginEnabled]
	public void Dispose()
	{
		if (!TryTakeOwnership() || RefuseStaleRuntime())
		{
			return;
		}

		try
		{
			_ = ReleaseTakenWithOutcome();
		}
		catch (Exception)
		{
			if (_lastReleaseOutcome.Status == TargetReleaseStatus.Unspecified)
			{
				_lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(null);
			}
			// IDisposable cleanup must not hide another failure or retry a possibly partial CE deallocation.
		}
	}

	/// <summary>
	///     Releases the target allocation and reports every failure to the caller.
	/// </summary>
	/// <exception cref="ObjectDisposedException">Ownership was already released or disposed.</exception>
	/// <exception cref="InvalidOperationException">
	///     The allocation belongs to a previous Lua runtime identity (ownership consumed without any CE call), or a
	///     lifecycle violation prevented the deallocation from beginning (ownership consumed; see
	///     <see cref="LastReleaseOutcome" />).
	/// </exception>
	/// <exception cref="EngineOperationFailedException">Cheat Engine reported that deallocation did not complete.</exception>
	/// <exception cref="EngineTargetIdentityException">The current target is not the captured process incarnation.</exception>
	/// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or non-callable.</exception>
	/// <exception cref="EngineBindingException">The CE binding cannot uphold its documented contract.</exception>
	/// <exception cref="EngineMarshallingException">The binding returned an invalid success/failure shape.</exception>
	/// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
	/// <remarks>
	///     Ownership is consumed before the CE call. If the call throws, the region remains disposed and cannot be retried;
	///     this is safer than attempting to free an allocation whose native state is unknown.
	/// </remarks>
	[RequiresPluginEnabled]
	public void Release()
	{
		if (!TryTakeOwnership())
		{
			ThrowDisposed();
		}

		if (RefuseStaleRuntime())
		{
			ThrowRuntimeChanged();
		}

		TargetMemoryOperationOutcome outcome = ReleaseTakenWithOutcome();
		if (outcome.IsSuccess)
		{
			return;
		}

		ThrowForReleaseOutcome(outcome);
	}

	/// <summary>
	///     Releases the target allocation and returns a structured factual outcome instead of translating an expected
	///     or Engine-boundary result into an exception.
	/// </summary>
	/// <returns>The outcome of the one permitted deallocation attempt.</returns>
	/// <remarks>
	///     Ownership is consumed before the CE call just as it is for <see cref="Release" />. This method does not
	///     retry an expected failure or a boundary failure. It exposes the allocation binding outcome and a separate
	///     <see cref="LastReleaseOutcome" /> for the target-incarnation check without inspecting exception text.
	/// </remarks>
	/// <exception cref="ObjectDisposedException">Ownership was already released or disposed.</exception>
	/// <exception cref="InvalidOperationException">
	///     The allocation belongs to a previous Lua runtime identity (ownership consumed without any CE call), or a
	///     lifecycle violation prevented the deallocation from beginning (ownership consumed; see
	///     <see cref="LastReleaseOutcome" />).
	/// </exception>
	[RequiresPluginEnabled]
	public TargetMemoryOperationOutcome ReleaseWithOutcome()
	{
		if (!TryTakeOwnership())
		{
			ThrowDisposed();
		}

		if (RefuseStaleRuntime())
		{
			ThrowRuntimeChanged();
		}

		return ReleaseTakenWithOutcome();
	}

	/// <summary>
	///     Releases this owner and returns the target-bound outcome. Never throws once ownership was taken.
	/// </summary>
	/// <returns>
	///     <see cref="TargetReleaseStatus.Released" /> after a confirmed deallocation; a target refusal
	///     (<see cref="TargetReleaseStatus.RefusedNoTarget" />, <see cref="TargetReleaseStatus.RefusedTargetChanged" />,
	///     <see cref="TargetReleaseStatus.RefusedProcessReused" />,
	///     <see cref="TargetReleaseStatus.RefusedIdentityUnavailable" />);
	///     <see cref="TargetReleaseStatus.RefusedRuntimeChanged" /> after a re-enable or a controlled state replacement;
	///     <see cref="TargetReleaseStatus.NotInvoked" /> when a detached runtime prevented the call; or
	///     <see cref="TargetReleaseStatus.UnconfirmedAfterInvocation" /> after an attempted deallocation that CE did not
	///     confirm.
	/// </returns>
	/// <exception cref="ObjectDisposedException">Ownership was already released or disposed.</exception>
	[RequiresPluginEnabled]
	public TargetReleaseOutcome ReleaseWithTargetOutcome()
	{
		if (!TryTakeOwnership())
		{
			ThrowDisposed();
		}

		if (!RefuseStaleRuntime())
		{
			try
			{
				_ = ReleaseTakenWithOutcome();
			}
			catch (Exception)
			{
				// ReleaseTakenWithOutcome has recorded the factual outcome of the failure; nothing is retried.
			}
		}

		return LastReleaseOutcome;
	}

	// Refuses, without any CE call, an allocation whose Lua universe is no longer current. The comparison is identity
	// only: an "is attached" pre-check would break consumer implementations that run without an attached runtime.
	private bool RefuseStaleRuntime()
	{
		if (EngineResourceOrigin.IsCurrent(Origin.Runtime))
		{
			return false;
		}

		_lastReleaseOutcome = TargetReleaseOutcome.RefusedRuntimeChanged();
		return true;
	}

	private TargetMemoryOperationOutcome ReleaseTakenWithOutcome()
	{
		try
		{
			TargetMemoryOperationOutcome outcome = _targetBoundOperations.DeallocateBoundWithOutcome(TargetIncarnation,
				_address, _size,
				out TargetIdentityCheck targetCheck);
			_lastReleaseOutcome = targetCheck.IsCurrent
				? outcome.IsSuccess
					? TargetReleaseOutcome.Released()
					: TargetReleaseOutcome.Unconfirmed(outcome.FailureKind)
				: TargetReleaseOutcome.Refused(targetCheck);
			return outcome;
		}
		catch (InvalidOperationException) when (!LuaRuntime.IsAttached ||
												!EngineResourceOrigin.IsCurrent(Origin.Runtime))
		{
			// The binding could not acquire a Lua operation: deAlloc never ran.
			_lastReleaseOutcome = TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure);
			throw;
		}
		catch (EngineException exception)
		{
			_lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(exception.Kind);
			throw;
		}
		catch (Exception)
		{
			// A consumer implementation may have started an effect before it threw.
			_lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(null);
			throw;
		}
	}

	private void ThrowForReleaseOutcome(TargetMemoryOperationOutcome outcome)
	{
		if (outcome.Kind is TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable or
			TargetMemoryOperationOutcomeKind.TargetIdentityMismatch)
		{
			throw new EngineTargetIdentityException("TargetMemoryDeallocate",
				LastReleaseOutcome.TargetCheck.GetValueOrDefault());
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.ExpectedFailure)
		{
			throw new EngineOperationFailedException("TargetMemoryDeallocate");
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.GlobalUnavailable)
		{
			throw new EngineGlobalUnavailableException("TargetMemoryDeallocate");
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.CapabilityUnavailable)
		{
			throw new EngineCapabilityUnavailableException("TargetMemoryAllocation");
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.ProtectedLuaFailure)
		{
			throw new EngineLuaException("TargetMemoryDeallocate", outcome.LuaStatus);
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.MarshallingFailure)
		{
			throw new EngineMarshallingException("TargetMemoryDeallocate", EngineMarshallingDirection.Result,
				"a Boolean deallocation result", "a non-Boolean result");
		}

		throw new EngineBindingException("TargetMemoryDeallocate");
	}

	private bool TryTakeOwnership()
	{
		return Interlocked.Exchange(ref _released, 1) == 0;
	}

	private void ThrowIfReleased()
	{
		if (IsDisposed)
		{
			ThrowDisposed();
		}
	}

	private static void ThrowDisposed()
	{
		throw new ObjectDisposedException(nameof(AllocatedRegion),
			"The target allocation is no longer owned: it was released or disposed.");
	}

	private static void ThrowRuntimeChanged()
	{
		throw new InvalidOperationException(
			"The target allocation belongs to a previous Lua runtime identity; ownership was consumed without deallocating it.");
	}
}
