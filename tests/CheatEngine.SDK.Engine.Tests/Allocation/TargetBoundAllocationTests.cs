using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>Deterministic target-routing regressions for allocation owners without a live Cheat Engine host.</summary>
public sealed class TargetBoundAllocationTests
{
    [Fact]
    public void Release_on_the_original_qualified_target_routes_once_to_that_target()
    {
        TargetContext first = new(4101, 1001);
        TargetContext second = new(4102, 2002);
        ControlledTargetOperations operations = new(first);
        TargetMemoryAllocator allocator = new(operations);

        var region = allocator.Allocate(CreateRequest());
        region.Release();

        Assert.Equal(1, first.AllocationCalls);
        Assert.Equal(1, first.DeallocationCalls);
        Assert.Equal(0, second.DeallocationCalls);
        Assert.Equal(TargetReleaseStatus.Released, region.LastReleaseOutcome.Status);
        Assert.Equal(0, operations.ExternalSelectionTransitions);
    }

    [Fact]
    public void Dispose_after_an_external_target_switch_refuses_cleanup_without_selecting_or_deallocating()
    {
        TargetContext first = new(4101, 1001);
        TargetContext second = new(4102, 2002);
        ControlledTargetOperations operations = new(first);
        TargetMemoryAllocator allocator = new(operations);
        var region = allocator.Allocate(CreateRequest());

        operations.SelectExternally(second);
        region.Dispose();
        region.Dispose();

        Assert.True(region.IsDisposed);
        Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, region.LastReleaseOutcome.Status);
        Assert.Equal(0, first.DeallocationCalls);
        Assert.Equal(0, second.DeallocationCalls);
        Assert.Same(second, operations.Current);
        Assert.Equal(1, operations.ExternalSelectionTransitions);
    }

    [Fact]
    public void Release_after_an_external_target_switch_reports_the_refusal_without_deallocating_either_target()
    {
        TargetContext first = new(4101, 1001);
        TargetContext second = new(4102, 2002);
        ControlledTargetOperations operations = new(first);
        TargetMemoryAllocator allocator = new(operations);
        var region = allocator.Allocate(CreateRequest());

        operations.SelectExternally(second);
        var exception = Assert.Throws<EngineTargetIdentityException>(region.Release);

        Assert.Equal(TargetIdentityCheckKind.TargetChanged, exception.Check.Kind);
        Assert.Equal(0, first.DeallocationCalls);
        Assert.Equal(0, second.DeallocationCalls);
        Assert.Same(second, operations.Current);
        Assert.Throws<ObjectDisposedException>(region.Release);
    }

    [Fact]
    public void Dispose_after_an_external_A_to_B_to_A_switch_uses_only_the_current_original_incarnation()
    {
        TargetContext first = new(4101, 1001);
        TargetContext second = new(4102, 2002);
        ControlledTargetOperations operations = new(first);
        TargetMemoryAllocator allocator = new(operations);
        var region = allocator.Allocate(CreateRequest());

        operations.SelectExternally(second);
        operations.SelectExternally(first);
        region.Dispose();

        Assert.Equal(TargetReleaseStatus.Released, region.LastReleaseOutcome.Status);
        Assert.Equal(1, first.DeallocationCalls);
        Assert.Equal(0, second.DeallocationCalls);
        Assert.Same(first, operations.Current);
        Assert.Equal(2, operations.ExternalSelectionTransitions);
    }

    [Fact]
    public void Release_after_target_termination_refuses_cleanup_and_records_no_target()
    {
        TargetContext first = new(4101, 1001);
        ControlledTargetOperations operations = new(first);
        TargetMemoryAllocator allocator = new(operations);
        var region = allocator.Allocate(CreateRequest());

        operations.TerminateCurrent();
        var outcome = region.ReleaseWithTargetOutcome();

        Assert.Equal(TargetReleaseStatus.RefusedNoTarget, outcome.Status);
        Assert.Equal(0, first.DeallocationCalls);
        Assert.Null(operations.Current);
        region.Dispose();
        Assert.Equal(0, first.DeallocationCalls);
    }

    [Fact]
    public void Release_after_PID_reuse_refuses_the_new_incarnation()
    {
        TargetContext original = new(4101, 1001);
        TargetContext reused = new(4101, 3003);
        ControlledTargetOperations operations = new(original);
        TargetMemoryAllocator allocator = new(operations);
        var region = allocator.Allocate(CreateRequest());

        operations.ReusePid(reused);
        var outcome = region.ReleaseWithTargetOutcome();

        Assert.Equal(TargetReleaseStatus.RefusedProcessReused, outcome.Status);
        Assert.Equal(0, original.DeallocationCalls);
        Assert.Equal(0, reused.DeallocationCalls);
        Assert.Same(reused, operations.Current);
    }

    [Fact]
    public void Allocate_when_target_identity_is_unavailable_refuses_before_the_effectful_operation()
    {
        ControlledTargetOperations operations = new(current: null);
        TargetMemoryAllocator allocator = new(operations);

        var exception = Assert.Throws<EngineTargetIdentityException>(() => allocator.Allocate(CreateRequest()));
        var outcome = allocator.AllocateWithOutcome(CreateRequest());

        Assert.Equal(TargetIdentityCheckKind.NoTargetSelected, exception.Check.Kind);
        Assert.Equal(TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable, outcome.Operation.Kind);
        Assert.Equal(0, operations.TotalAllocationCalls);
        Assert.Equal(0, operations.TotalDeallocationCalls);
    }

    private static TargetAllocationRequest CreateRequest()
    {
        return new TargetAllocationRequest(new TargetAllocationSize(4096));
    }

    private sealed class ControlledTargetOperations : ITargetMemoryAllocationOperations,
        ITargetBoundMemoryAllocationOperations
    {
        public ControlledTargetOperations(TargetContext? current)
        {
            Current = current;
        }

        public TargetContext? Current { get; private set; }

        public int ExternalSelectionTransitions { get; private set; }

        public int TotalAllocationCalls { get; private set; }

        public int TotalDeallocationCalls { get; private set; }

        public void SelectExternally(TargetContext target)
        {
            Current = target;
            ExternalSelectionTransitions++;
        }

        public void TerminateCurrent()
        {
            Current = null;
            ExternalSelectionTransitions++;
        }

        public void ReusePid(TargetContext replacement)
        {
            Current = replacement;
            ExternalSelectionTransitions++;
        }

        public bool TryAllocate(TargetAllocationRequest request, out Address address)
        {
            var outcome = AllocateBoundWithOutcome(request, out _, out _);
            address = outcome.Address;
            return outcome.IsSuccess;
        }

        public bool TryDeallocate(Address address, TargetAllocationSize size)
        {
            if (Current is null) return false;

            Current.DeallocationCalls++;
            TotalDeallocationCalls++;
            return true;
        }

        public TargetMemoryAllocationOutcome AllocateBoundWithOutcome(TargetAllocationRequest request,
            out TargetProcessIncarnation incarnation, out TargetSelectionObservation observation)
        {
            observation = ObserveCurrent();
            incarnation = observation.Incarnation.GetValueOrDefault();
            if (!observation.IsQualified)
                return TargetMemoryAllocationOutcome.FromOperation(TargetMemoryOperationOutcome.FromFailureKind(
                    EngineFailureKind.TargetIdentityUnavailable));

            Current!.AllocationCalls++;
            TotalAllocationCalls++;
            return TargetMemoryAllocationOutcome.Succeeded(new Address(0x7FF6_1000_0000));
        }

        public bool TryDeallocateBound(TargetProcessIncarnation expected, Address address, TargetAllocationSize size,
            out TargetIdentityCheck targetCheck)
        {
            var outcome = DeallocateBoundWithOutcome(expected, address, size, out targetCheck);
            return targetCheck.IsCurrent && outcome.IsSuccess;
        }

        public TargetMemoryOperationOutcome DeallocateBoundWithOutcome(TargetProcessIncarnation expected, Address address,
            TargetAllocationSize size, out TargetIdentityCheck targetCheck)
        {
            var observation = ObserveCurrent();
            targetCheck = Check(expected, observation);
            if (!targetCheck.IsCurrent)
                return TargetMemoryOperationOutcome.FromFailureKind(targetCheck.Kind is TargetIdentityCheckKind.TargetChanged
                    or TargetIdentityCheckKind.ProcessReused
                    ? EngineFailureKind.TargetIdentityMismatch
                    : EngineFailureKind.TargetIdentityUnavailable);

            return TryDeallocate(address, size)
                ? TargetMemoryOperationOutcome.Succeeded()
                : TargetMemoryOperationOutcome.ExpectedFailure();
        }

        private TargetSelectionObservation ObserveCurrent()
        {
            return Current is null
                ? TargetSelectionObservation.NoTarget()
                : TargetSelectionObservation.Qualified(Current.Incarnation);
        }

        private static TargetIdentityCheck Check(TargetProcessIncarnation expected,
            TargetSelectionObservation observation)
        {
            if (!observation.IsQualified)
                return TargetSelection.CreateUnavailableCheck(observation);

            var current = observation.Incarnation.GetValueOrDefault();
            if (current.ProcessId != expected.ProcessId)
                return new TargetIdentityCheck(TargetIdentityCheckKind.TargetChanged, observation);

            return current.StartedAtUtcTicks == expected.StartedAtUtcTicks
                ? new TargetIdentityCheck(TargetIdentityCheckKind.Current, observation)
                : new TargetIdentityCheck(TargetIdentityCheckKind.ProcessReused, observation);
        }
    }

    private sealed class TargetContext
    {
        public TargetContext(int processId, long startedAtUtcTicks)
        {
            Incarnation = new TargetProcessIncarnation(processId, startedAtUtcTicks);
        }

        public TargetProcessIncarnation Incarnation { get; }

        public int AllocationCalls { get; set; }

        public int DeallocationCalls { get; set; }
    }
}
