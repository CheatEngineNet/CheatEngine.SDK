using System.Reflection;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     The allocation facade converts only documented expected CE failures and preserves technical boundary failures.
/// </summary>
public sealed class TargetMemoryAllocatorTests
{
	[Fact]
	public void Allocate_when_owner_publication_fails_compensates_once_and_exposes_the_confirmed_cleanup()
	{
		AllocationOperationsFake operations = new();
		TargetMemoryAllocator allocator = new(operations);
		InvalidOperationException cause = new("injected region publication failure");

		EngineResourceHandoffException exception = Assert.Throws<EngineResourceHandoffException>(() =>
			allocator.AllocateCore(
				new TargetAllocationRequest(new TargetAllocationSize(4096)),
				(_, _, _, _, _) => throw cause));

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(TargetReleaseStatus.Released, exception.CleanupOutcome.Status);
		Assert.Equal(1, operations.AllocateCalls);
		Assert.Equal(1, operations.DeallocateCalls);
		Assert.Equal(operations.AllocatedAddress, operations.LastDeallocatedAddress);
	}

	[Fact]
	public void Allocate_when_owner_publication_and_compensation_fail_reports_an_unconfirmed_effect_without_retrying()
	{
		AllocationOperationsFake operations = new() { DeallocationResult = false };
		TargetMemoryAllocator allocator = new(operations);

		EngineResourceHandoffException exception = Assert.Throws<EngineResourceHandoffException>(() =>
			allocator.AllocateCore(
				new TargetAllocationRequest(new TargetAllocationSize(4096)),
				static (_, _, _, _, _) => throw new InvalidOperationException("injected region publication failure")));

		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, exception.CleanupOutcome.Status);
		Assert.Equal(EngineFailureKind.ExpectedOperationFailure, exception.CleanupOutcome.FailureKind);
		Assert.Equal(1, operations.AllocateCalls);
		Assert.Equal(1, operations.DeallocateCalls);
	}

	[Fact]
	public void
		Allocate_when_owner_publication_and_compensation_raise_keeps_the_primary_cause_and_marks_the_effect_unknown()
	{
		EngineLuaException cleanupFailure = new("TargetMemoryDeallocate", LuaStatus.RuntimeError);
		AllocationOperationsFake operations = new() { DeallocationException = cleanupFailure };
		TargetMemoryAllocator allocator = new(operations);
		InvalidOperationException cause = new("injected region publication failure");

		EngineResourceHandoffException exception = Assert.Throws<EngineResourceHandoffException>(() =>
			allocator.AllocateCore(
				new TargetAllocationRequest(new TargetAllocationSize(4096)),
				(_, _, _, _, _) => throw cause));

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, exception.CleanupOutcome.Status);
		Assert.Equal(EngineFailureKind.ProtectedLuaFailure, exception.CleanupOutcome.FailureKind);
		Assert.Equal(1, operations.DeallocateCalls);
	}

	[Fact]
	public void Allocate_when_owner_publication_observes_a_replacement_target_refuses_compensation_without_touching_it()
	{
		AllocationOperationsFake operations = new();
		TargetMemoryAllocator allocator = new(operations);
		TargetProcessIncarnation replacement = new(4343, 2);

		EngineResourceHandoffException exception = Assert.Throws<EngineResourceHandoffException>(() =>
			allocator.AllocateCore(
				new TargetAllocationRequest(new TargetAllocationSize(4096)),
				(_, _, _, _, _) =>
				{
					operations.TargetObservation = TargetSelectionObservation.Qualified(replacement);
					throw new InvalidOperationException("injected region publication failure");
				}));

		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, exception.CleanupOutcome.Status);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Fact]
	public void Allocate_on_success_returns_an_owned_region_and_forwards_the_full_request()
	{
		AllocationOperationsFake operations = new() { AllocatedAddress = new Address(0x7FF6_1234_0000) };
		TargetMemoryAllocator allocator = new(operations);
		TargetAllocationRequest request = new(new TargetAllocationSize(8192), new Address(0x7FF6_1200_0000),
			MemoryProtection.ExecuteReadWrite);

		using AllocatedRegion region = allocator.Allocate(request);

		Assert.Equal(new Address(0x7FF6_1234_0000), region.Address);
		Assert.Equal(new TargetAllocationSize(8192), region.Size);
		Assert.Equal(request, operations.LastRequest);
		Assert.Equal(1, operations.AllocateCalls);
	}

	[Fact]
	public void Allocate_when_CE_reports_expected_failure_throws_the_stable_expected_failure()
	{
		AllocationOperationsFake operations = new() { AllocationResult = false, AllocatedAddress = Address.Zero };
		TargetMemoryAllocator allocator = new(operations);

		EngineOperationFailedException exception = Assert.Throws<EngineOperationFailedException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

		Assert.Equal("TargetMemoryAllocate", exception.Operation);
		Assert.Null(exception.InnerException);
		Assert.Equal(1, operations.AllocateCalls);
	}

	[Fact]
	public void Allocate_when_CE_reports_success_without_an_address_preserves_the_unknown_effect_diagnostic()
	{
		AllocationOperationsFake operations = new() { AllocatedAddress = Address.Zero };
		TargetMemoryAllocator allocator = new(operations);

		EngineResourceHandoffException exception = Assert.Throws<EngineResourceHandoffException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, exception.CleanupOutcome.Status);
		Assert.Equal(EngineFailureKind.MarshallingFailure, exception.CleanupOutcome.FailureKind);
		Assert.IsType<EngineMarshallingException>(exception.InnerException);
		Assert.Equal(1, operations.AllocateCalls);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Fact]
	public void Allocate_when_CE_reports_failure_with_an_address_throws_marshalling()
	{
		AllocationOperationsFake operations =
			new() { AllocationResult = false, AllocatedAddress = new Address(0x1234) };
		TargetMemoryAllocator allocator = new(operations);

		EngineMarshallingException exception = Assert.Throws<EngineMarshallingException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

		Assert.Equal("TargetMemoryAllocate", exception.Operation);
		Assert.Equal(1, operations.AllocateCalls);
	}

	[Fact]
	public void Allocate_when_the_binding_fails_preserves_the_binding_exception()
	{
		EngineBindingException failure = new("TargetMemoryAllocate",
			"the generated binding returned an incompatible result");
		AllocationOperationsFake operations = new() { AllocationException = failure };
		TargetMemoryAllocator allocator = new(operations);

		EngineBindingException thrown = Assert.Throws<EngineBindingException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

		Assert.Same(failure, thrown);
	}

	[Fact]
	public void Allocate_when_the_required_global_is_unavailable_preserves_that_distinct_failure()
	{
		EngineGlobalUnavailableException failure = new("TargetMemoryAllocate");
		AllocationOperationsFake operations = new() { AllocationException = failure };
		TargetMemoryAllocator allocator = new(operations);

		EngineGlobalUnavailableException thrown = Assert.Throws<EngineGlobalUnavailableException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

		Assert.Same(failure, thrown);
		Assert.Equal(EngineFailureKind.GlobalUnavailable, thrown.Kind);
	}

	[Fact]
	public void Allocate_when_the_protected_lua_call_fails_preserves_the_EngineLuaException()
	{
		EngineLuaException failure = new("TargetMemoryAllocate", LuaStatus.RuntimeError);
		AllocationOperationsFake operations = new() { AllocationException = failure };
		TargetMemoryAllocator allocator = new(operations);

		EngineLuaException thrown = Assert.Throws<EngineLuaException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

		Assert.Same(failure, thrown);
	}

	[Fact]
	public void Allocate_with_only_the_compatibility_seam_refuses_an_unqualified_owner_before_an_effectful_call()
	{
		DirectOnlyAllocationOperations operations = new();
		TargetMemoryAllocator allocator = new(operations);
		TargetAllocationRequest request = new(new TargetAllocationSize(4096));

		EngineTargetIdentityException exception =
			Assert.Throws<EngineTargetIdentityException>(() => allocator.Allocate(request));

		Assert.Equal(TargetIdentityCheckKind.CurrentTargetUnqualified, exception.Check.Kind);
		Assert.Equal(EngineFailureKind.TargetIdentityUnavailable, exception.Kind);
		Assert.Equal(0, operations.AllocateCalls);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Theory]
	[InlineData(EngineFailureKind.CapabilityUnavailable, typeof(EngineCapabilityUnavailableException))]
	[InlineData(EngineFailureKind.ProtectedLuaFailure, typeof(EngineLuaException))]
	[InlineData(EngineFailureKind.MarshallingFailure, typeof(EngineMarshallingException))]
	[InlineData(EngineFailureKind.TargetIdentityUnavailable, typeof(EngineTargetIdentityException))]
	[InlineData(EngineFailureKind.BindingFailure, typeof(EngineBindingException))]
	public void Allocate_with_a_target_bound_boundary_failure_throws_its_stable_public_exception(
		EngineFailureKind failureKind, Type expectedExceptionType)
	{
		AllocationOperationsFake operations = new()
		{
			BoundAllocationOutcomeOverride = TargetMemoryAllocationOutcome.Failed(
				TargetMemoryOperationOutcome.Failed(failureKind, LuaStatus.SyntaxError))
		};
		TargetMemoryAllocator allocator = new(operations);

		EngineException exception = Assert.ThrowsAny<EngineException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

		Assert.IsType(expectedExceptionType, exception);
		Assert.Equal(0, operations.AllocateCalls);
		Assert.Equal(0, operations.DeallocateCalls);

		if (exception is EngineLuaException lua)
		{
			Assert.Equal(LuaStatus.SyntaxError, lua.Status);
		}

		if (exception is EngineMarshallingException marshalling)
		{
			Assert.Equal(EngineMarshallingDirection.Result, marshalling.Direction);
		}

		if (exception is EngineTargetIdentityException identity)
		{
			Assert.Equal(TargetIdentityCheckKind.CurrentTargetUnqualified, identity.Check.Kind);
		}
	}

	[Fact]
	public void ReleaseWithOutcome_adapts_the_legacy_bool_seam_and_consumes_ownership()
	{
		AllocationOperationsFake operations = new() { DeallocationResult = false };
		TargetMemoryAllocator allocator = new(operations);
		AllocatedRegion region = allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096)));

		TargetMemoryOperationOutcome outcome = region.ReleaseWithOutcome();

		Assert.Equal(TargetMemoryOperationOutcomeKind.ExpectedFailure, outcome.Kind);
		Assert.Equal(EngineFailureKind.ExpectedOperationFailure, outcome.FailureKind);
		Assert.True(region.IsDisposed);
		Assert.Equal(1, operations.DeallocateCalls);
	}

	[Fact]
	public void Public_target_memory_operations_carry_enabled_lifecycle_metadata_without_an_unproven_thread_claim()
	{
		MethodInfo allocate = typeof(TargetMemoryAllocator)
			.GetMethod(nameof(TargetMemoryAllocator.Allocate))!;
		MethodInfo tryAllocateOwned = typeof(TargetMemoryAllocator)
			.GetMethod(nameof(TargetMemoryAllocator.TryAllocate))!;
		MethodInfo release = typeof(AllocatedRegion).GetMethod(nameof(AllocatedRegion.Release))!;
		MethodInfo dispose = typeof(AllocatedRegion).GetMethod(nameof(AllocatedRegion.Dispose))!;
		MethodInfo tryAllocate = typeof(ITargetMemoryAllocationOperations)
			.GetMethod(nameof(ITargetMemoryAllocationOperations.TryAllocate))!;
		MethodInfo tryDeallocate = typeof(ITargetMemoryAllocationOperations).GetMethod(
			nameof(ITargetMemoryAllocationOperations.TryDeallocate))!;
		MethodInfo releaseWithOutcome = typeof(AllocatedRegion).GetMethod(nameof(AllocatedRegion.ReleaseWithOutcome))!;
		MethodInfo releaseWithTargetOutcome =
			typeof(AllocatedRegion).GetMethod(nameof(AllocatedRegion.ReleaseWithTargetOutcome))!;

		AssertHasLifecycleMetadata(allocate);
		AssertHasLifecycleMetadata(tryAllocateOwned);
		AssertHasLifecycleMetadata(release);
		AssertHasLifecycleMetadata(dispose);
		AssertHasLifecycleMetadata(tryAllocate);
		AssertHasLifecycleMetadata(tryDeallocate);
		AssertHasLifecycleMetadata(releaseWithOutcome);
		AssertHasLifecycleMetadata(releaseWithTargetOutcome);
	}

	[Fact]
	public void No_public_allocation_entry_point_returns_an_address_without_an_owner()
	{
		Assert.False(typeof(LuaTargetMemoryAllocationOperations).IsPublic);
		Assert.Null(typeof(TargetMemoryAllocator).Assembly.GetType(
			"CheatEngine.SDK.Engine.Allocation.ITargetMemoryAllocationOutcomeOperations"));
		Assert.Null(typeof(TargetMemoryAllocator).GetMethod("AllocateWithOutcome"));
		Assert.DoesNotContain(typeof(ITargetMemoryAllocationOperations).GetMethods(), static method =>
			method.Name is not (nameof(ITargetMemoryAllocationOperations.TryAllocate) or
				nameof(ITargetMemoryAllocationOperations.TryDeallocate)));
	}

	[Fact]
	public void TryAllocate_success_returns_an_owner_with_its_incarnation_and_runtime_origin()
	{
		AllocationOperationsFake operations = new() { AllocatedAddress = new Address(0x7FF6_1234_0000) };
		TargetMemoryAllocator allocator = new(operations);
		TargetAllocationRequest request = new(new TargetAllocationSize(8192));

		TargetAllocationAcquireOutcome outcome = allocator.TryAllocate(request, out AllocatedRegion? region);

		Assert.True(outcome.HasOwner);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		Assert.Null(outcome.Compensation);
		Assert.True(outcome.Allocation.IsSuccess);
		Assert.Equal(new Address(0x7FF6_1234_0000), outcome.Allocation.Address);
		Assert.True(outcome.TargetObservation.IsQualified);
		AllocatedRegion owner = Assert.IsType<AllocatedRegion>(region);
		Assert.Equal(new Address(0x7FF6_1234_0000), owner.Address);
		Assert.Equal(outcome.TargetObservation.Incarnation, owner.Origin.Target);
		Assert.Equal(owner.TargetIncarnation, owner.Origin.Target);
		Assert.Equal(LuaRuntime.CurrentStateIdentity, owner.Origin.Runtime);
		Assert.True(owner.Origin.IsTargetBound);
		Assert.Equal(request, operations.LastRequest);
		owner.Dispose();
		Assert.Equal(TargetReleaseStatus.Released, owner.LastReleaseOutcome.Status);
		Assert.Equal(1, operations.AllocateCalls);
		Assert.Equal(1, operations.DeallocateCalls);
	}

	[Fact]
	public void TryAllocate_expected_failure_reports_not_applied_without_an_owner()
	{
		AllocationOperationsFake operations = new() { AllocationResult = false, AllocatedAddress = Address.Zero };
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome =
			allocator.TryAllocate(new TargetAllocationRequest(new TargetAllocationSize(4096)),
				out AllocatedRegion? region);

		Assert.Null(region);
		Assert.False(outcome.HasOwner);
		Assert.Equal(EngineEffectState.NotApplied, outcome.Effect);
		Assert.Equal(TargetMemoryOperationOutcomeKind.ExpectedFailure, outcome.Allocation.Operation.Kind);
		Assert.Equal(Address.Zero, outcome.Allocation.Address);
		Assert.Null(outcome.Compensation);
		Assert.Equal(1, operations.AllocateCalls);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Fact]
	public void TryAllocate_unqualified_target_reports_not_started_before_the_effectful_call()
	{
		AllocationOperationsFake operations = new()
		{
			TargetObservation = TargetSelectionObservation.Unqualified(4242)
		};
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome =
			allocator.TryAllocate(new TargetAllocationRequest(new TargetAllocationSize(4096)),
				out AllocatedRegion? region);

		Assert.Null(region);
		Assert.False(outcome.HasOwner);
		Assert.Equal(EngineEffectState.NotStarted, outcome.Effect);
		Assert.Equal(TargetSelectionObservationStatus.CurrentTargetUnqualified, outcome.TargetObservation.Status);
		Assert.Equal(TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable, outcome.Allocation.Operation.Kind);
		Assert.Null(outcome.Compensation);
		Assert.Equal(0, operations.AllocateCalls);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Fact]
	[Trait("Qualification", "Q08.a")]
	public void TryAllocate_publication_failure_reports_exactly_one_compensation()
	{
		AllocationOperationsFake operations = new();
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome = allocator.TryAllocateCore(
			new TargetAllocationRequest(new TargetAllocationSize(4096)),
			static (_, _, _, _, _) => throw new InvalidOperationException("injected region publication failure"),
			out AllocatedRegion? region);

		Assert.Null(region);
		Assert.False(outcome.HasOwner);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		Assert.Equal(TargetReleaseStatus.Released, outcome.Compensation.GetValueOrDefault().Status);
		Assert.Equal(operations.AllocatedAddress, outcome.Allocation.Address);
		Assert.Equal(1, operations.AllocateCalls);
		Assert.Equal(1, operations.DeallocateCalls);
		Assert.Equal(operations.AllocatedAddress, operations.LastDeallocatedAddress);
	}

	[Fact]
	public void TryAllocate_publication_and_compensation_failure_keeps_the_allocated_address_for_manual_recovery()
	{
		AllocationOperationsFake operations = new()
		{
			AllocatedAddress = new Address(0x7FF6_5555_0000), DeallocationResult = false
		};
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome = allocator.TryAllocateCore(
			new TargetAllocationRequest(new TargetAllocationSize(4096)),
			static (_, _, _, _, _) => throw new InvalidOperationException("injected region publication failure"),
			out AllocatedRegion? region);

		Assert.Null(region);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		TargetReleaseOutcome compensation = Assert.NotNull(outcome.Compensation);
		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, compensation.Status);
		Assert.Equal(EngineFailureKind.ExpectedOperationFailure, compensation.FailureKind);
		Assert.True(compensation.RequiresManualRecovery);
		Assert.Equal(new Address(0x7FF6_5555_0000), outcome.Allocation.Address);
		Assert.Equal(1, operations.DeallocateCalls);
	}

	[Fact]
	public void TryAllocate_with_only_the_compatibility_seam_refuses_without_an_effect()
	{
		DirectOnlyAllocationOperations operations = new();
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome =
			allocator.TryAllocate(new TargetAllocationRequest(new TargetAllocationSize(4096)),
				out AllocatedRegion? region);

		Assert.Null(region);
		Assert.False(outcome.HasOwner);
		Assert.Equal(EngineEffectState.NotStarted, outcome.Effect);
		Assert.Equal(TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable, outcome.Allocation.Operation.Kind);
		Assert.Equal(TargetSelectionObservationStatus.Unspecified, outcome.TargetObservation.Status);
		Assert.Null(outcome.Compensation);
		Assert.Equal(0, operations.AllocateCalls);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Fact]
	public void TryAllocate_success_without_an_address_is_a_marshalling_failure_with_unknown_effect()
	{
		AllocationOperationsFake operations = new() { AllocatedAddress = Address.Zero };
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome =
			allocator.TryAllocate(new TargetAllocationRequest(new TargetAllocationSize(4096)),
				out AllocatedRegion? region);

		Assert.Null(region);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Equal(TargetMemoryOperationOutcomeKind.MarshallingFailure, outcome.Allocation.Operation.Kind);
		Assert.Null(outcome.Compensation);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Fact]
	public void TryAllocate_failure_that_still_carries_an_address_keeps_it_and_reports_unknown_effect()
	{
		AllocationOperationsFake operations = new()
		{
			AllocationResult = false, AllocatedAddress = new Address(0x1234)
		};
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome =
			allocator.TryAllocate(new TargetAllocationRequest(new TargetAllocationSize(4096)),
				out AllocatedRegion? region);

		Assert.Null(region);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Equal(TargetMemoryOperationOutcomeKind.MarshallingFailure, outcome.Allocation.Operation.Kind);
		Assert.Equal(new Address(0x1234), outcome.Allocation.Address);
	}

	[Fact]
	public void TryAllocate_on_an_unqualified_target_after_the_effect_refuses_compensation_and_keeps_the_address()
	{
		AllocationOperationsFake operations = new()
		{
			AllocatedAddress = new Address(0x7FF6_7777_0000),
			TargetObservation = TargetSelectionObservation.Unqualified(4242),
			AllocateEvenWhenUnqualified = true
		};
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome =
			allocator.TryAllocate(new TargetAllocationRequest(new TargetAllocationSize(4096)),
				out AllocatedRegion? region);

		Assert.Null(region);
		Assert.False(outcome.HasOwner);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		TargetReleaseOutcome compensation = Assert.NotNull(outcome.Compensation);
		Assert.Equal(TargetReleaseStatus.RefusedIdentityUnavailable, compensation.Status);
		Assert.Equal(new Address(0x7FF6_7777_0000), outcome.Allocation.Address);
		Assert.Equal(1, operations.AllocateCalls);
		Assert.Equal(0, operations.DeallocateCalls);
	}

	[Theory]
	[InlineData(EngineFailureKind.GlobalUnavailable, EngineEffectState.NotStarted)]
	[InlineData(EngineFailureKind.TargetIdentityUnavailable, EngineEffectState.NotStarted)]
	[InlineData(EngineFailureKind.ProtectedLuaFailure, EngineEffectState.Unknown)]
	[InlineData(EngineFailureKind.BindingFailure, EngineEffectState.Unknown)]
	[InlineData(EngineFailureKind.MarshallingFailure, EngineEffectState.Unknown)]
	public void TryAllocate_turns_a_binding_exception_into_an_outcome_without_parsing_its_text(
		EngineFailureKind failureKind, EngineEffectState expectedEffect)
	{
		EngineException failure = failureKind switch
		{
			EngineFailureKind.GlobalUnavailable => new EngineGlobalUnavailableException("TargetMemoryAllocate"),
			EngineFailureKind.TargetIdentityUnavailable => new EngineTargetIdentityException("TargetMemoryAllocate",
				TargetSelection.CreateUnavailableCheck(TargetSelectionObservation.NoTarget())),
			EngineFailureKind.ProtectedLuaFailure => new EngineLuaException("TargetMemoryAllocate",
				LuaStatus.SyntaxError,
				"A deliberately irrelevant localized message."),
			EngineFailureKind.BindingFailure => new EngineBindingException("TargetMemoryAllocate", "incompatible"),
			_ => new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result, "a", "b")
		};
		AllocationOperationsFake operations = new() { AllocationException = failure };
		TargetMemoryAllocator allocator = new(operations);

		TargetAllocationAcquireOutcome outcome =
			allocator.TryAllocate(new TargetAllocationRequest(new TargetAllocationSize(4096)),
				out AllocatedRegion? region);

		Assert.Null(region);
		Assert.Equal(expectedEffect, outcome.Effect);
		Assert.Equal(failureKind, outcome.Allocation.Operation.FailureKind);
		Assert.Equal(Address.Zero, outcome.Allocation.Address);
		if (failureKind == EngineFailureKind.ProtectedLuaFailure)
		{
			Assert.Equal(LuaStatus.SyntaxError, outcome.Allocation.Operation.LuaStatus);
		}
	}

	[Fact]
	public void TryAllocate_rejects_an_invalid_request_before_any_call()
	{
		AllocationOperationsFake operations = new();
		TargetMemoryAllocator allocator = new(operations);

		Assert.Throws<ArgumentOutOfRangeException>(() => allocator.TryAllocate(default, out _));
		Assert.Equal(0, operations.AllocateCalls);
	}

	private static void AssertHasLifecycleMetadata(MethodInfo method)
	{
		Assert.True(Attribute.IsDefined(method, typeof(RequiresPluginEnabledAttribute)));
		Assert.False(Attribute.IsDefined(method, typeof(MainThreadOnlyAttribute)));
	}

	private sealed class DirectOnlyAllocationOperations : ITargetMemoryAllocationOperations
	{
		public int AllocateCalls
		{
			get;
			private set;
		}

		public int DeallocateCalls
		{
			get;
			private set;
		}

		public bool TryAllocate(TargetAllocationRequest request, out Address address)
		{
			AllocateCalls++;
			address = new Address(0x7FF6_1000_0000);
			return true;
		}

		public bool TryDeallocate(Address address, TargetAllocationSize size)
		{
			DeallocateCalls++;
			return true;
		}
	}
}
