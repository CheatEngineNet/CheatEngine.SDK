using System.Reflection;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     The allocation facade converts only documented expected CE failures and preserves technical boundary failures.
/// </summary>
public sealed class TargetMemoryAllocatorTests
{
    [Fact]
    public void Allocate_on_success_returns_an_owned_region_and_forwards_the_full_request()
    {
        AllocationOperationsFake operations = new() { AllocatedAddress = new Address(0x7FF6_1234_0000) };
        TargetMemoryAllocator allocator = new(operations);
        TargetAllocationRequest request = new(new TargetAllocationSize(8192), new Address(0x7FF6_1200_0000),
            MemoryProtection.ExecuteReadWrite);

        using var region = allocator.Allocate(request);

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

        var exception = Assert.Throws<EngineOperationFailedException>(() =>
            allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

        Assert.Equal("TargetMemoryAllocate", exception.Operation);
        Assert.Null(exception.InnerException);
        Assert.Equal(1, operations.AllocateCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Allocate_when_the_success_shape_contains_a_null_or_inconsistent_address_throws_marshalling(bool result)
    {
        AllocationOperationsFake operations = new() { AllocationResult = result, AllocatedAddress = Address.Zero };
        if (!result) operations.AllocatedAddress = new Address(0x1234);
        TargetMemoryAllocator allocator = new(operations);

        var exception = Assert.Throws<EngineMarshallingException>(() =>
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

        var thrown = Assert.Throws<EngineBindingException>(() =>
            allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void Allocate_when_the_required_global_is_unavailable_preserves_that_distinct_failure()
    {
        EngineGlobalUnavailableException failure = new("TargetMemoryAllocate");
        AllocationOperationsFake operations = new() { AllocationException = failure };
        TargetMemoryAllocator allocator = new(operations);

        var thrown = Assert.Throws<EngineGlobalUnavailableException>(() =>
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

        var thrown = Assert.Throws<EngineLuaException>(() =>
            allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void AllocateWithOutcome_adapts_the_legacy_bool_seam_without_parsing_exception_text()
    {
        EngineLuaException failure = new("TargetMemoryAllocate", LuaStatus.SyntaxError,
            "A deliberately irrelevant localized message.");
        AllocationOperationsFake operations = new() { AllocationException = failure };
        TargetMemoryAllocator allocator = new(operations);

        var outcome = allocator.AllocateWithOutcome(new TargetAllocationRequest(new TargetAllocationSize(4096)));

        Assert.Equal(TargetMemoryOperationOutcomeKind.ProtectedLuaFailure, outcome.Operation.Kind);
        Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.Operation.FailureKind);
        Assert.Equal(LuaStatus.SyntaxError, outcome.Operation.LuaStatus);
        Assert.Equal(Address.Zero, outcome.Address);
        Assert.Equal(1, operations.AllocateCalls);
    }

    [Fact]
    public void AllocateWithOutcome_adapts_legacy_expected_failure_without_creating_an_owner()
    {
        AllocationOperationsFake operations = new() { AllocationResult = false, AllocatedAddress = Address.Zero };
        TargetMemoryAllocator allocator = new(operations);

        var outcome = allocator.AllocateWithOutcome(new TargetAllocationRequest(new TargetAllocationSize(4096)));

        Assert.Equal(TargetMemoryOperationOutcomeKind.ExpectedFailure, outcome.Operation.Kind);
        Assert.Equal(EngineFailureKind.ExpectedOperationFailure, outcome.Operation.FailureKind);
        Assert.Equal(Address.Zero, outcome.Address);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(1, operations.AllocateCalls);
    }

    [Fact]
    public void Allocate_with_only_the_compatibility_seam_refuses_an_unqualified_owner_before_an_effectful_call()
    {
        DirectOnlyAllocationOperations operations = new();
        TargetMemoryAllocator allocator = new(operations);
        var request = new TargetAllocationRequest(new TargetAllocationSize(4096));

        var exception = Assert.Throws<EngineTargetIdentityException>(() => allocator.Allocate(request));
        var outcome = allocator.AllocateWithOutcome(request);

        Assert.Equal(TargetIdentityCheckKind.CurrentTargetUnqualified, exception.Check.Kind);
        Assert.Equal(EngineFailureKind.TargetIdentityUnavailable, exception.Kind);
        Assert.Equal(TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable, outcome.Operation.Kind);
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
            BoundAllocationOutcomeOverride = TargetMemoryAllocationOutcome.FromOperation(
                TargetMemoryOperationOutcome.FromFailureKind(failureKind, LuaStatus.SyntaxError)),
        };
        TargetMemoryAllocator allocator = new(operations);

        var exception = Assert.ThrowsAny<EngineException>(() =>
            allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));

        Assert.IsType(expectedExceptionType, exception);
        Assert.Equal(0, operations.AllocateCalls);
        Assert.Equal(0, operations.DeallocateCalls);

        if (exception is EngineLuaException lua)
            Assert.Equal(LuaStatus.SyntaxError, lua.Status);
        if (exception is EngineMarshallingException marshalling)
            Assert.Equal(EngineMarshallingDirection.Result, marshalling.Direction);
        if (exception is EngineTargetIdentityException identity)
            Assert.Equal(TargetIdentityCheckKind.CurrentTargetUnqualified, identity.Check.Kind);
    }

    [Fact]
    public void ReleaseWithOutcome_adapts_the_legacy_bool_seam_and_consumes_ownership()
    {
        AllocationOperationsFake operations = new() { DeallocationResult = false };
        TargetMemoryAllocator allocator = new(operations);
        var region = allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096)));

        var outcome = region.ReleaseWithOutcome();

        Assert.Equal(TargetMemoryOperationOutcomeKind.ExpectedFailure, outcome.Kind);
        Assert.Equal(EngineFailureKind.ExpectedOperationFailure, outcome.FailureKind);
        Assert.True(region.IsDisposed);
        Assert.Equal(1, operations.DeallocateCalls);
    }

    [Fact]
    public void Public_target_memory_operations_carry_enabled_lifecycle_metadata_without_an_unproven_thread_claim()
    {
        var allocate = typeof(TargetMemoryAllocator)
            .GetMethod(nameof(TargetMemoryAllocator.Allocate))!;
        var release = typeof(AllocatedRegion).GetMethod(nameof(AllocatedRegion.Release))!;
        var dispose = typeof(AllocatedRegion).GetMethod(nameof(AllocatedRegion.Dispose))!;
        var tryAllocate = typeof(ITargetMemoryAllocationOperations)
            .GetMethod(nameof(ITargetMemoryAllocationOperations.TryAllocate))!;
        var tryDeallocate = typeof(ITargetMemoryAllocationOperations).GetMethod(
            nameof(ITargetMemoryAllocationOperations.TryDeallocate))!;
        var allocateWithOutcome = typeof(ITargetMemoryAllocationOutcomeOperations).GetMethod(
            nameof(ITargetMemoryAllocationOutcomeOperations.AllocateWithOutcome))!;
        var deallocateWithOutcome = typeof(ITargetMemoryAllocationOutcomeOperations).GetMethod(
            nameof(ITargetMemoryAllocationOutcomeOperations.DeallocateWithOutcome))!;
        var facadeOutcome = typeof(TargetMemoryAllocator).GetMethod(nameof(TargetMemoryAllocator.AllocateWithOutcome))!;
        var releaseWithOutcome = typeof(AllocatedRegion).GetMethod(nameof(AllocatedRegion.ReleaseWithOutcome))!;

        AssertHasLifecycleMetadata(allocate);
        AssertHasLifecycleMetadata(release);
        AssertHasLifecycleMetadata(dispose);
        AssertHasLifecycleMetadata(tryAllocate);
        AssertHasLifecycleMetadata(tryDeallocate);
        AssertHasLifecycleMetadata(allocateWithOutcome);
        AssertHasLifecycleMetadata(deallocateWithOutcome);
        AssertHasLifecycleMetadata(facadeOutcome);
        AssertHasLifecycleMetadata(releaseWithOutcome);
    }

    private static void AssertHasLifecycleMetadata(MethodInfo method)
    {
        Assert.True(Attribute.IsDefined(method, typeof(RequiresPluginEnabledAttribute)));
        Assert.False(Attribute.IsDefined(method, typeof(MainThreadOnlyAttribute)));
    }

    private sealed class DirectOnlyAllocationOperations : ITargetMemoryAllocationOperations
    {
        public int AllocateCalls { get; private set; }

        public int DeallocateCalls { get; private set; }

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
