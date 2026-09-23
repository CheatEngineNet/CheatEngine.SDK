using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     Allocates explicitly owned regions in the attached target process through the CE 7.7 allocation contract.
/// </summary>
/// <remarks>
///     <para>
///         This facade validates the strongly typed request. <see cref="Allocate" /> makes an expected CE failure explicit
///         as <see cref="EngineOperationFailedException" />; <see cref="TryAllocate" /> reports every result as a
///         <see cref="TargetAllocationAcquireOutcome" /> instead. Protected Lua, binding, and marshalling failures keep
///         their canonical Engine categories. The generated CE binding is supplied through
///         <see cref="ITargetMemoryAllocationOperations" />. An owned allocation additionally requires the implementation
///         to opt in to <see cref="ITargetBoundMemoryAllocationOperations" />; a legacy direct-operation implementation
///         is never silently used to create an owner with an unverified cleanup target.
///     </para>
///     <para>
///         An allocation is bound to the Lua runtime identity and the target incarnation that created it
///         (<see cref="AllocatedRegion.Origin" />). After a re-enable or a controlled state replacement the region refuses
///         cleanup and reports the residue; it never reopens or reselects a target. No public SDK entry point yields a
///         live allocation without an owner: when Cheat Engine allocated but no owner can be published, the allocator
///         makes exactly one compensation attempt and reports it.
///     </para>
/// </remarks>
public sealed class TargetMemoryAllocator
{
	private const string AllocateOperation = "TargetMemoryAllocate";

	private readonly ITargetMemoryAllocationOperations _operations;

	/// <summary>
	///     Initializes an allocator backed by the production CE 7.7 <c>allocateMemory</c>/<c>deAlloc</c> binding.
	/// </summary>
	/// <remarks>
	///     The binding resolves its globals only while an enabled plugin has a Lua state. Constructing this facade does
	///     not contact Cheat Engine and is safe before plugin enable; <see cref="Allocate" /> and
	///     <see cref="TryAllocate" /> remain lifecycle-gated.
	/// </remarks>
	public TargetMemoryAllocator()
		: this(LuaTargetMemoryAllocationOperations.Instance)
	{
	}

	/// <summary>
	///     Initializes the target-memory allocation facade.
	/// </summary>
	/// <param name="operations">The CE 7.7 generated-binding-facing operations.</param>
	/// <exception cref="ArgumentNullException"><paramref name="operations" /> is <see langword="null" />.</exception>
	public TargetMemoryAllocator(ITargetMemoryAllocationOperations operations)
	{
		ArgumentNullException.ThrowIfNull(operations);
		_operations = operations;
	}

	/// <summary>
	///     Allocates a region in the current target process and transfers sole ownership to the returned wrapper.
	/// </summary>
	/// <param name="request">The allocation size, optional target base preference, and optional initial protection.</param>
	/// <returns>The explicitly owned allocation.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="request" /> has the default or an invalid size.</exception>
	/// <exception cref="EngineOperationFailedException">Cheat Engine reported that allocation did not complete.</exception>
	/// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or non-callable.</exception>
	/// <exception cref="EngineBindingException">The CE binding cannot uphold its documented contract.</exception>
	/// <exception cref="EngineMarshallingException">The binding returned an invalid success/failure shape.</exception>
	/// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
	/// <exception cref="EngineTargetIdentityException">The operation cannot qualify a target incarnation.</exception>
	/// <exception cref="EngineResourceHandoffException">
	///     Cheat Engine accepted an allocation but an owner could not be published (including a Lua runtime identity change
	///     during the call); <see cref="EngineResourceHandoffException.CleanupOutcome" /> records the one
	///     target-qualified compensation attempt or an unconfirmed effect when no address was available.
	/// </exception>
	[RequiresPluginEnabled]
	public AllocatedRegion Allocate(TargetAllocationRequest request)
	{
		return AllocateCore(request, CreateRegion);
	}

	/// <summary>
	///     Attempts an allocation and publishes an owner only when Cheat Engine returned an address for a qualified
	///     target in the runtime that made the call. Never throws an Engine failure: every result is an outcome.
	/// </summary>
	/// <param name="request">The allocation size, optional target base preference, and optional initial protection.</param>
	/// <param name="region">
	///     The sole owner of the allocation when <see cref="TargetAllocationAcquireOutcome.HasOwner" /> is
	///     <see langword="true" />; otherwise <see langword="null" />.
	/// </param>
	/// <returns>
	///     The factual outcome. <see cref="TargetAllocationAcquireOutcome.Effect" /> is
	///     <see cref="EngineEffectState.NotStarted" /> when no allocation call began (only the compatibility seam
	///     <see cref="ITargetMemoryAllocationOperations" /> is available, the target is not qualified, the global is
	///     unavailable), <see cref="EngineEffectState.NotApplied" /> for Cheat Engine's documented <c>nil</c>,
	///     <see cref="EngineEffectState.Applied" /> when Cheat Engine returned an address, and
	///     <see cref="EngineEffectState.Unknown" /> for a protected failure or a malformed result. When Cheat Engine
	///     allocated but no owner is published, <see cref="TargetAllocationAcquireOutcome.Compensation" /> reports the one
	///     compensation attempt and the address stays readable for manual recovery.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="request" /> has the default or an invalid size.</exception>
	/// <exception cref="InvalidOperationException">
	///     A lifecycle violation surfaced by the binding: the plugin is not enabled or the calling thread has no Lua
	///     state.
	/// </exception>
	[RequiresPluginEnabled]
	public TargetAllocationAcquireOutcome TryAllocate(TargetAllocationRequest request, out AllocatedRegion? region)
	{
		return TryAllocateCore(request, CreateRegion, out region);
	}

	// The factory is internal so tests can fail publication after the effect without allowing consumers to choose a
	// different ownership policy. Keep the target-bound tuple local until the owner has been published.
	internal AllocatedRegion AllocateCore(TargetAllocationRequest request, AllocatedRegionFactory factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ValidateRequest(request);
		if (_operations is not ITargetBoundMemoryAllocationOperations targetBound)
		{
			throw new EngineTargetIdentityException(AllocateOperation, GetUnavailableTargetCheck());
		}

		LuaStateIdentity runtime = LuaRuntime.CurrentStateIdentity;
		TargetMemoryAllocationOutcome allocationOutcome = targetBound.AllocateBoundWithOutcome(request,
			out TargetProcessIncarnation incarnation, out TargetSelectionObservation observation);
		bool allocated = allocationOutcome.IsSuccess;
		Address address = allocationOutcome.Address;
		if (!allocated)
		{
			if (!observation.IsQualified)
			{
				throw new EngineTargetIdentityException(AllocateOperation,
					TargetSelection.CreateUnavailableCheck(observation));
			}

			if (!address.IsZero)
			{
				throw new EngineMarshallingException(AllocateOperation, EngineMarshallingDirection.Result,
					"a null target address on failure", "a nonzero target address on failure");
			}

			ThrowForAllocationOutcome(allocationOutcome.Operation);
		}

		if (address.IsZero)
		{
			ThrowUnknownSuccessfulAllocation();
		}

		if (!observation.IsQualified)
		{
			ThrowUnqualifiedSuccessfulAllocation(observation);
		}

		if (!EngineResourceOrigin.IsCurrent(runtime))
		{
			TargetReleaseOutcome refused = CompensateFailedPublication(targetBound, address, request.Size, incarnation,
				runtime);
			throw new EngineResourceHandoffException(AllocateOperation, refused, CreateRuntimeChangedCause());
		}

		try
		{
			return factory(targetBound, address, request.Size, incarnation, runtime);
		}
		catch (Exception exception)
		{
			TargetReleaseOutcome cleanupOutcome =
				CompensateFailedPublication(targetBound, address, request.Size, incarnation, runtime);
			throw new EngineResourceHandoffException(AllocateOperation, cleanupOutcome, exception);
		}
	}

	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "The allocation, effect classification and owner publication form one transaction.")]
	internal TargetAllocationAcquireOutcome TryAllocateCore(TargetAllocationRequest request,
		AllocatedRegionFactory factory, out AllocatedRegion? region)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ValidateRequest(request);
		region = null;
		if (_operations is not ITargetBoundMemoryAllocationOperations targetBound)
		{
			return new TargetAllocationAcquireOutcome(Failed(EngineFailureKind.TargetIdentityUnavailable),
				EngineEffectState.NotStarted, default, null, false);
		}

		// Captured before the bound call; the call's own admitted operation is compared with it afterwards, so an
		// attach or state replacement that raced with the call is detected and never yields an owner.
		LuaStateIdentity runtime = LuaRuntime.CurrentStateIdentity;
		TargetMemoryAllocationOutcome allocation;
		TargetProcessIncarnation incarnation;
		TargetSelectionObservation observation;
		try
		{
			allocation = targetBound.AllocateBoundWithOutcome(request, out incarnation, out observation);
		}
		catch (EngineException exception)
		{
			EngineEffectState effect = exception.Kind is EngineFailureKind.GlobalUnavailable or
				EngineFailureKind.TargetIdentityUnavailable
				? EngineEffectState.NotStarted
				: EngineEffectState.Unknown;
			return new TargetAllocationAcquireOutcome(TargetMemoryAllocationOutcome.Failed(CreateOutcome(exception)),
				effect, default, null, false);
		}

		if (!allocation.IsSuccess)
		{
			return ClassifyFailedAllocation(allocation, observation);
		}

		Address address = allocation.Address;
		if (address.IsZero)
		{
			return new TargetAllocationAcquireOutcome(Failed(EngineFailureKind.MarshallingFailure),
				EngineEffectState.Unknown, observation, null, false);
		}

		if (!observation.IsQualified)
		{
			// No deAlloc against a target that cannot be qualified: the refusal is the one compensation result.
			return new TargetAllocationAcquireOutcome(allocation, EngineEffectState.Applied, observation,
				TargetReleaseOutcome.Refused(TargetSelection.CreateUnavailableCheck(observation)), false);
		}

		if (!EngineResourceOrigin.IsCurrent(runtime))
		{
			return new TargetAllocationAcquireOutcome(allocation, EngineEffectState.Applied, observation,
				CompensateFailedPublication(targetBound, address, request.Size, incarnation, runtime), false);
		}

		AllocatedRegion? published;
		try
		{
			published = factory(targetBound, address, request.Size, incarnation, runtime);
		}
		catch (Exception)
		{
			published = null;
		}

		if (published is null)
		{
			return new TargetAllocationAcquireOutcome(allocation, EngineEffectState.Applied, observation,
				CompensateFailedPublication(targetBound, address, request.Size, incarnation, runtime), false);
		}

		region = published;
		return new TargetAllocationAcquireOutcome(allocation, EngineEffectState.Applied, observation, null, true);
	}

	internal static TargetMemoryOperationOutcome CreateOutcome(EngineException exception)
	{
		return exception is EngineLuaException lua
			? TargetMemoryOperationOutcome.Failed(exception.Kind, lua.Status)
			: TargetMemoryOperationOutcome.Failed(exception.Kind);
	}

	private static TargetAllocationAcquireOutcome ClassifyFailedAllocation(TargetMemoryAllocationOutcome allocation,
		TargetSelectionObservation observation)
	{
		if (!observation.IsQualified)
		{
			return new TargetAllocationAcquireOutcome(allocation, EngineEffectState.NotStarted, observation, null,
				false);
		}

		if (!allocation.Address.IsZero)
		{
			// A failure that still carries an address is contradictory: keep the address for manual recovery and do
			// not claim that nothing happened.
			return new TargetAllocationAcquireOutcome(
				new TargetMemoryAllocationOutcome(
					TargetMemoryOperationOutcome.Failed(EngineFailureKind.MarshallingFailure), allocation.Address),
				EngineEffectState.Unknown, observation, null, false);
		}

		EngineEffectState effect = allocation.Operation.Kind switch
		{
			TargetMemoryOperationOutcomeKind.ExpectedFailure => EngineEffectState.NotApplied,
			TargetMemoryOperationOutcomeKind.GlobalUnavailable or TargetMemoryOperationOutcomeKind.CapabilityUnavailable
				or TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable
				or TargetMemoryOperationOutcomeKind.TargetIdentityMismatch => EngineEffectState.NotStarted,
			_ => EngineEffectState.Unknown
		};
		return new TargetAllocationAcquireOutcome(allocation, effect, observation, null, false);
	}

	private static TargetMemoryAllocationOutcome Failed(EngineFailureKind failureKind)
	{
		return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(failureKind));
	}

	private static void ValidateRequest(TargetAllocationRequest request)
	{
		if (request.Size.Value <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.Size.Value,
				"An allocation request must have a positive size.");
		}
	}

	private static AllocatedRegion CreateRegion(ITargetBoundMemoryAllocationOperations operations, Address address,
		TargetAllocationSize size, TargetProcessIncarnation targetIncarnation, LuaStateIdentity runtime)
	{
		return new AllocatedRegion(operations, address, size, targetIncarnation, runtime);
	}

	// The one compensation attempt for an allocation whose owner could not be published. It is refused without a call
	// when the Lua runtime that made the allocation is no longer current, and reports NotInvoked when the runtime
	// detached before the deallocation could begin (a lifecycle exception from the bound operations).
	private static TargetReleaseOutcome CompensateFailedPublication(ITargetBoundMemoryAllocationOperations operations,
		Address address, TargetAllocationSize size, TargetProcessIncarnation targetIncarnation,
		LuaStateIdentity runtime)
	{
		if (!EngineResourceOrigin.IsCurrent(runtime))
		{
			return TargetReleaseOutcome.RefusedRuntimeChanged();
		}

		try
		{
			TargetMemoryOperationOutcome outcome = operations.DeallocateBoundWithOutcome(targetIncarnation, address,
				size, out TargetIdentityCheck targetCheck);
			if (!targetCheck.IsCurrent)
			{
				return TargetReleaseOutcome.Refused(targetCheck);
			}

			return outcome.IsSuccess
				? TargetReleaseOutcome.Released()
				: TargetReleaseOutcome.Unconfirmed(outcome.FailureKind);
		}
		catch (InvalidOperationException) when (!LuaRuntime.IsAttached || !EngineResourceOrigin.IsCurrent(runtime))
		{
			return TargetReleaseOutcome.NotInvoked(EngineFailureKind.BindingFailure);
		}
		catch (EngineException exception)
		{
			return TargetReleaseOutcome.Unconfirmed(exception.Kind);
		}
		catch (Exception)
		{
			return TargetReleaseOutcome.Unconfirmed(null);
		}
	}

	private static InvalidOperationException CreateRuntimeChangedCause()
	{
		return new InvalidOperationException(
			"The Lua runtime identity changed while the allocation was made, so no owner can be published for it.");
	}

	[DoesNotReturn]
	private static void ThrowUnknownSuccessfulAllocation()
	{
		EngineMarshallingException cause = new(AllocateOperation, EngineMarshallingDirection.Result,
			"a nonzero target address on success", "a null target address on success");
		throw new EngineResourceHandoffException(AllocateOperation,
			TargetReleaseOutcome.Unconfirmed(EngineFailureKind.MarshallingFailure), cause);
	}

	[DoesNotReturn]
	private static void ThrowUnqualifiedSuccessfulAllocation(TargetSelectionObservation observation)
	{
		TargetIdentityCheck check = TargetSelection.CreateUnavailableCheck(observation);
		EngineTargetIdentityException cause = new(AllocateOperation, check);
		throw new EngineResourceHandoffException(AllocateOperation, TargetReleaseOutcome.Refused(check), cause);
	}

	private static void ThrowForAllocationOutcome(TargetMemoryOperationOutcome outcome)
	{
		if (outcome.Kind == TargetMemoryOperationOutcomeKind.ExpectedFailure)
		{
			throw new EngineOperationFailedException(AllocateOperation);
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.GlobalUnavailable)
		{
			throw new EngineGlobalUnavailableException(AllocateOperation);
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.CapabilityUnavailable)
		{
			throw new EngineCapabilityUnavailableException("TargetMemoryAllocation");
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.ProtectedLuaFailure)
		{
			throw new EngineLuaException(AllocateOperation, outcome.LuaStatus);
		}

		if (outcome.Kind == TargetMemoryOperationOutcomeKind.MarshallingFailure)
		{
			throw new EngineMarshallingException(AllocateOperation, EngineMarshallingDirection.Result,
				"a target address or nil", "a result that is neither an address nor nil");
		}

		if (outcome.Kind is TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable or
		    TargetMemoryOperationOutcomeKind.TargetIdentityMismatch)
		{
			throw new EngineTargetIdentityException(AllocateOperation, GetUnavailableTargetCheck());
		}

		throw new EngineBindingException(AllocateOperation);
	}

	private static TargetIdentityCheck GetUnavailableTargetCheck()
	{
		TargetSelectionObservation observation = TargetSelectionObservation.FromStatus(
			TargetSelectionObservationStatus.CurrentTargetUnqualified);
		return TargetSelection.CreateUnavailableCheck(observation);
	}
}
