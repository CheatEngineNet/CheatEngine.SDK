using System.Text;

using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     Allocation owners across the plugin lifecycle, through the production binding on the Lua fixture: detach between
///     allocation and publication, release after detach, release after re-enable or a state replacement, and a later
///     allocation that reuses a released address.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AllocationLifecycleTests
{
	private static readonly TargetAllocationRequest SRequest = new(new TargetAllocationSize(4096));

	[Fact]
	[Trait("Qualification", "Q08.a")]
	public void Detach_between_allocation_and_publication_compensates_once_and_reports_not_invoked()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();

		TargetAllocationAcquireOutcome outcome = allocator.TryAllocateCore(SRequest, static (_, _, _, _, _) =>
		{
			LuaRuntime.Detach();
			throw new InvalidOperationException("the plugin was disabled before the owner was published");
		}, out AllocatedRegion? region);

		Assert.False(LuaRuntime.IsAttached);
		Assert.Null(region);
		Assert.False(outcome.HasOwner);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		Assert.Equal(new Address(0x7FF6_1000_0000), outcome.Allocation.Address);
		TargetReleaseOutcome compensation = Assert.NotNull(outcome.Compensation);
		Assert.Equal(TargetReleaseStatus.NotInvoked, compensation.Status);
		Assert.Equal(EngineFailureKind.BindingFailure, compensation.FailureKind);
		Assert.True(compensation.RequiresManualRecovery);
		AssertCounts(L, 1, 0);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Allocate_with_a_detach_before_publication_throws_the_handoff_with_a_not_invoked_compensation()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();

		EngineResourceHandoffException exception = Assert.Throws<EngineResourceHandoffException>(() =>
			allocator.AllocateCore(SRequest, static (_, _, _, _, _) =>
			{
				LuaRuntime.Detach();
				throw new InvalidOperationException("the plugin was disabled before the owner was published");
			}));

		Assert.Equal(TargetReleaseStatus.NotInvoked, exception.CleanupOutcome.Status);
		Assert.IsType<InvalidOperationException>(exception.InnerException);
		AssertCounts(L, 1, 0);
	}

	[Fact]
	public void Dispose_after_detach_reports_not_invoked_and_consumes_ownership()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		AllocatedRegion region = new TargetMemoryAllocator().Allocate(SRequest);

		LuaRuntime.Detach();
		region.Dispose();
		region.Dispose();

		Assert.True(region.IsDisposed);
		Assert.Equal(TargetReleaseStatus.NotInvoked, region.LastReleaseOutcome.Status);
		Assert.Equal(EngineFailureKind.BindingFailure, region.LastReleaseOutcome.FailureKind);
		AssertCounts(L, 1, 0);
	}

	[Fact]
	public void ReleaseWithTargetOutcome_after_detach_reports_not_invoked()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		AllocatedRegion region = new TargetMemoryAllocator().Allocate(SRequest);

		LuaRuntime.Detach();
		TargetReleaseOutcome outcome = region.ReleaseWithTargetOutcome();

		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.Status);
		Assert.Equal(EngineFailureKind.BindingFailure, outcome.FailureKind);
		Assert.True(region.IsDisposed);
		Assert.Throws<ObjectDisposedException>(() => region.ReleaseWithTargetOutcome());
		AssertCounts(L, 1, 0);
	}

	[Fact]
	public void Release_after_detach_throws_the_lifecycle_failure_and_records_not_invoked()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		AllocatedRegion region = new TargetMemoryAllocator().Allocate(SRequest);

		LuaRuntime.Detach();

		Assert.Throws<InvalidOperationException>(region.Release);
		Assert.True(region.IsDisposed);
		Assert.Equal(TargetReleaseStatus.NotInvoked, region.LastReleaseOutcome.Status);
		AssertCounts(L, 1, 0);
	}

	[Fact]
	public void Dispose_after_reattach_refuses_cleanup_in_the_new_runtime()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		AllocatedRegion region = new TargetMemoryAllocator().Allocate(SRequest);
		EngineResourceOrigin origin = region.Origin;

		LuaRuntime.Detach();
		LuaRuntime.Attach(scope.Binding);
		region.Dispose();

		Assert.True(region.IsDisposed);
		Assert.Equal(origin, region.Origin);
		Assert.False(region.Origin.IsCurrentRuntime);
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, region.LastReleaseOutcome.Status);
		Assert.True(region.LastReleaseOutcome.RequiresManualRecovery);
		AssertCounts(L, 1, 0);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Release_after_a_controlled_state_replacement_refuses_cleanup_and_throws_the_lifecycle_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		AllocatedRegion region = new TargetMemoryAllocator().Allocate(SRequest);

		FakeHost.ReplaceStateGeneration();

		Assert.Throws<InvalidOperationException>(region.Release);
		Assert.True(region.IsDisposed);
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, region.LastReleaseOutcome.Status);
		AssertCounts(L, 1, 0);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.e")]
	public void Released_region_address_cannot_free_a_later_allocation_at_the_same_address()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();
		AllocatedRegion first = allocator.Allocate(SRequest);
		first.Release();

		AllocatedRegion second = allocator.Allocate(SRequest);
		Assert.Equal(new Address(0x7FF6_1000_0000), second.Address);
		first.Dispose();
		Assert.Throws<ObjectDisposedException>(first.Release);
		Assert.Throws<ObjectDisposedException>(() => first.ReleaseWithOutcome());
		Assert.Throws<ObjectDisposedException>(() => first.ReleaseWithTargetOutcome());

		Assert.False(second.IsDisposed);
		AssertCounts(L, 2, 1);
		second.Release();
		AssertCounts(L, 2, 2);
		Assert.Equal(TargetReleaseStatus.Released, first.LastReleaseOutcome.Status);
		Assert.Equal(TargetReleaseStatus.Released, second.LastReleaseOutcome.Status);
		Assert.Equal(0, L.Top);
	}

	private static void InstallAllocationGlobals(LuaState state)
	{
		FakeHost.InstallQualifiedLocalTarget(state);
		EngineTest.Run(state, """
		                      allocation_calls = 0
		                      deallocation_calls = 0
		                      function allocateMemory(size)
		                        allocation_calls = allocation_calls + 1
		                        return 0x7FF610000000
		                      end
		                      function deAlloc(address, size)
		                        deallocation_calls = deallocation_calls + 1
		                        return true
		                      end
		                      """u8);
	}

	private static void AssertCounts(LuaState state, long allocations, long deallocations)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes(
			"assert(allocation_calls == " + allocations + ", 'allocation calls: ' .. allocation_calls)\n" +
			"assert(deallocation_calls == " + deallocations + ", 'deallocation calls: ' .. deallocation_calls)"));
	}
}
