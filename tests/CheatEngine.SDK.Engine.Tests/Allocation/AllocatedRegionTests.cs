using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     Explicit allocation ownership: exactly one release attempt and an inert owner after every completion or failure.
/// </summary>
public sealed class AllocatedRegionTests
{
    [Fact]
    public void Dispose_releases_the_original_target_address_and_size_exactly_once()
    {
        AllocationOperationsFake operations = new() { AllocatedAddress = new Address(0x7FF6_3000_0000) };
        var region = Allocate(operations, 12288);

        region.Dispose();
        region.Dispose();

        Assert.True(region.IsDisposed);
        Assert.Equal(1, operations.DeallocateCalls);
        Assert.Equal(new Address(0x7FF6_3000_0000), operations.LastDeallocatedAddress);
        Assert.Equal(new TargetAllocationSize(12288), operations.LastDeallocatedSize);
        Assert.Throws<ObjectDisposedException>(() => _ = region.Address);
        Assert.Throws<ObjectDisposedException>(() => _ = region.Size);
    }

    [Fact]
    public void Dispose_when_CE_reports_failure_is_no_throw_and_consumes_ownership()
    {
        AllocationOperationsFake operations = new() { DeallocationResult = false };
        var region = Allocate(operations, 4096);

        region.Dispose();
        region.Dispose();

        Assert.True(region.IsDisposed);
        Assert.Equal(1, operations.DeallocateCalls);
    }

    [Fact]
    public void Release_when_CE_reports_failure_throws_the_expected_failure_and_never_retries()
    {
        AllocationOperationsFake operations = new() { DeallocationResult = false };
        var region = Allocate(operations, 4096);

        var exception = Assert.Throws<EngineOperationFailedException>(region.Release);

        Assert.Equal("TargetMemoryDeallocate", exception.Operation);
        Assert.True(region.IsDisposed);
        region.Dispose();
        Assert.Equal(1, operations.DeallocateCalls);
        Assert.Throws<ObjectDisposedException>(region.Release);
    }

    [Fact]
    public void Release_when_the_protected_lua_call_fails_preserves_the_failure_and_consumes_ownership()
    {
        EngineLuaException failure = new("TargetMemoryDeallocate", LuaStatus.RuntimeError);
        AllocationOperationsFake operations = new() { DeallocationException = failure };
        var region = Allocate(operations, 4096);

        var thrown = Assert.Throws<EngineLuaException>(region.Release);

        Assert.Same(failure, thrown);
        Assert.True(region.IsDisposed);
        region.Dispose();
        Assert.Equal(1, operations.DeallocateCalls);
    }

    [Fact]
    public void Release_when_the_required_global_is_unavailable_preserves_the_distinct_failure()
    {
        EngineGlobalUnavailableException failure = new("TargetMemoryDeallocate");
        AllocationOperationsFake operations = new() { DeallocationException = failure };
        var region = Allocate(operations, 4096);

        var thrown = Assert.Throws<EngineGlobalUnavailableException>(region.Release);

        Assert.Same(failure, thrown);
        Assert.Equal(EngineFailureKind.GlobalUnavailable, thrown.Kind);
        Assert.True(region.IsDisposed);
        Assert.Throws<ObjectDisposedException>(region.Release);
    }

    [Fact]
    public void Release_when_the_binding_contract_fails_preserves_the_failure_and_consumes_ownership()
    {
        EngineBindingException failure = new("TargetMemoryDeallocate",
            "the generated binding returned an incompatible result");
        AllocationOperationsFake operations = new() { DeallocationException = failure };
        var region = Allocate(operations, 4096);

        var thrown = Assert.Throws<EngineBindingException>(region.Release);

        Assert.Same(failure, thrown);
        Assert.True(region.IsDisposed);
        Assert.Throws<ObjectDisposedException>(region.Release);
    }

    [Fact]
    public void Release_when_the_result_cannot_be_marshalled_preserves_the_failure_and_consumes_ownership()
    {
        EngineMarshallingException failure = new("TargetMemoryDeallocate", EngineMarshallingDirection.Result,
            "a boolean", "a table");
        AllocationOperationsFake operations = new() { DeallocationException = failure };
        var region = Allocate(operations, 4096);

        var thrown = Assert.Throws<EngineMarshallingException>(region.Release);

        Assert.Same(failure, thrown);
        Assert.True(region.IsDisposed);
        Assert.Throws<ObjectDisposedException>(region.Release);
    }

    [Fact]
    public void Dispose_when_the_binding_fails_is_no_throw_and_consumes_ownership()
    {
        AllocationOperationsFake operations =
            new()
            {
                DeallocationException = new EngineBindingException("TargetMemoryDeallocate",
                    "the generated binding returned an incompatible result"),
            };
        var region = Allocate(operations, 4096);

        region.Dispose();

        Assert.True(region.IsDisposed);
        Assert.Equal(1, operations.DeallocateCalls);
    }

    [Fact]
    public void Dispose_when_an_EngineException_occurs_preserves_the_structured_failure_kind_and_consumes_ownership()
    {
        EngineException failure = new EngineLuaException("TargetMemoryDeallocate", LuaStatus.RuntimeError);
        AllocationOperationsFake operations = new() { DeallocationException = failure };
        var region = Allocate(operations, 4096);

        region.Dispose();
        region.Dispose();

        Assert.True(region.IsDisposed);
        Assert.Equal(1, operations.DeallocateCalls);
        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, region.LastReleaseOutcome.Status);
        Assert.Equal(EngineFailureKind.ProtectedLuaFailure, region.LastReleaseOutcome.FailureKind);
    }

    [Fact]
    public void Dispose_when_the_result_cannot_be_marshalled_is_no_throw_and_consumes_ownership()
    {
        AllocationOperationsFake operations =
            new()
            {
                DeallocationException = new EngineMarshallingException("TargetMemoryDeallocate",
                    EngineMarshallingDirection.Result, "a boolean", "a table"),
            };
        var region = Allocate(operations, 4096);

        region.Dispose();

        Assert.True(region.IsDisposed);
        Assert.Equal(1, operations.DeallocateCalls);
    }

    [Fact]
    public void Release_when_a_non_engine_deallocator_exception_occurs_records_an_unconfirmed_outcome_without_retrying()
    {
        AllocationOperationsFake operations = new()
        {
            DeallocationException = new InvalidOperationException("injected non-Engine deallocation failure"),
        };
        var region = Allocate(operations, 4096);

        Assert.Throws<InvalidOperationException>(region.Release);

        Assert.True(region.IsDisposed);
        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, region.LastReleaseOutcome.Status);
        Assert.Null(region.LastReleaseOutcome.FailureKind);
        region.Dispose();
        Assert.Equal(1, operations.DeallocateCalls);
    }

    [Fact]
    public void Concurrent_dispose_attempts_call_the_deallocator_once()
    {
        AllocationOperationsFake operations = new();
        var region = Allocate(operations, 4096);

        Parallel.Invoke(region.Dispose, region.Dispose);

        Assert.True(region.IsDisposed);
        Assert.Equal(1, operations.DeallocateCalls);
    }

    private static AllocatedRegion Allocate(AllocationOperationsFake operations, long size)
    {
        TargetMemoryAllocator allocator = new(operations);
        return allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(size)));
    }
}
