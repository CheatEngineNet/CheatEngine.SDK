using System.Text;

using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>Fixture contracts for the direct CE allocation/deallocation binding used by the default allocator.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaTargetMemoryAllocationOperationsTests
{
	[Fact]
	public void Default_allocator_passes_all_documented_allocation_arguments_and_releases_the_owned_region()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();
		TargetAllocationRequest request = new(new TargetAllocationSize(4096), new Address(0x7000),
			MemoryProtection.ExecuteReadWrite);

		using AllocatedRegion region = allocator.Allocate(request);

		Assert.Equal(new Address(0x7FF6_1000_0000), region.Address);
		Assert.Equal(new TargetAllocationSize(4096), region.Size);
		AssertLuaInteger(L, "allocation_argument_count", 3);
		AssertLuaInteger(L, "allocation_size", 4096);
		AssertLuaInteger(L, "allocation_preferred", 0x7000);
		AssertLuaInteger(L, "allocation_protection", (long) MemoryProtection.ExecuteReadWrite);

		region.Release();

		AssertLuaInteger(L, "deallocation_address", 0x7FF6_1000_0000);
		AssertLuaInteger(L, "deallocation_size", 4096);
		Assert.True(region.IsDisposed);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryAllocate_with_protection_but_no_preferred_address_preserves_the_nil_optional_slot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		LuaTargetMemoryAllocationOperations operations = LuaTargetMemoryAllocationOperations.Instance;
		TargetAllocationRequest request = new(new TargetAllocationSize(8192), null, MemoryProtection.ReadWrite);

		Assert.True(operations.TryAllocate(request, out Address address));
		Assert.Equal(new Address(0x7FF6_1000_0000), address);
		AssertLuaInteger(L, "allocation_argument_count", 3);
		AssertLuaBoolean(L, "allocation_preferred_is_nil", true);
		AssertLuaInteger(L, "allocation_protection", (long) MemoryProtection.ReadWrite);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryAllocate_nil_result_is_an_expected_failure_and_does_not_strand_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, "function allocateMemory() return nil end"u8);

		Assert.False(LuaTargetMemoryAllocationOperations.Instance.TryAllocate(
			new TargetAllocationRequest(new TargetAllocationSize(4096)), out Address address));
		Assert.Equal(Address.Zero, address);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[InlineData("function allocateMemory() return nil end", TargetMemoryOperationOutcomeKind.ExpectedFailure, 0)]
	[InlineData("function allocateMemory() error('fixture allocation failure') end",
		TargetMemoryOperationOutcomeKind.ProtectedLuaFailure, 2)]
	[InlineData("function allocateMemory() return true end", TargetMemoryOperationOutcomeKind.MarshallingFailure, 0)]
	public void AllocateWithOutcome_classifies_documented_result_and_execution_categories_without_error_text(
		string fixture, TargetMemoryOperationOutcomeKind expectedKind, int expectedLuaStatus)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, Encoding.UTF8.GetBytes(fixture));

		TargetMemoryAllocationOutcome outcome = LuaTargetMemoryAllocationOperations.AllocateWithOutcome(
			new TargetAllocationRequest(new TargetAllocationSize(4096)));

		Assert.Equal(expectedKind, outcome.Operation.Kind);
		Assert.Equal(new LuaStatus(expectedLuaStatus), outcome.Operation.LuaStatus);
		Assert.Equal(Address.Zero, outcome.Address);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void AllocateWithOutcome_missing_global_is_distinct_from_a_present_global_that_throws()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;

		TargetMemoryAllocationOutcome outcome = LuaTargetMemoryAllocationOperations.AllocateWithOutcome(
			new TargetAllocationRequest(new TargetAllocationSize(4096)));

		Assert.Equal(TargetMemoryOperationOutcomeKind.GlobalUnavailable, outcome.Operation.Kind);
		Assert.Equal(EngineFailureKind.GlobalUnavailable, outcome.Operation.FailureKind);
		Assert.Equal(LuaStatus.Ok, outcome.Operation.LuaStatus);
		Assert.Equal(Address.Zero, outcome.Address);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void AllocateWithOutcome_global_resolution_failure_preserves_the_protected_status()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  setmetatable(_G, {
		                    __index = function(_, name)
		                      if name == "allocateMemory" then error("fixture global lookup failure") end
		                    end
		                  })
		                  """u8);

		TargetMemoryAllocationOutcome outcome = LuaTargetMemoryAllocationOperations.AllocateWithOutcome(
			new TargetAllocationRequest(new TargetAllocationSize(4096)));

		Assert.Equal(TargetMemoryOperationOutcomeKind.ProtectedLuaFailure, outcome.Operation.Kind);
		Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.Operation.FailureKind);
		Assert.Equal(LuaStatus.RuntimeError, outcome.Operation.LuaStatus);
		Assert.Equal(Address.Zero, outcome.Address);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void AllocateWithOutcome_expected_nil_result_is_allocation_free_after_warmup()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, "function allocateMemory() return nil end"u8);
		TargetAllocationRequest request = new(new TargetAllocationSize(4096));
		TargetMemoryOperationOutcomeKind kind = TargetMemoryOperationOutcomeKind.Unspecified;
		Address address = Address.Zero;

		AllocationGate.AssertZero(() =>
		{
			TargetMemoryAllocationOutcome outcome =
				LuaTargetMemoryAllocationOperations.AllocateWithOutcome(request);
			kind = outcome.Operation.Kind;
			address = outcome.Address;
		});

		Assert.Equal(TargetMemoryOperationOutcomeKind.ExpectedFailure, kind);
		Assert.Equal(Address.Zero, address);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Allocate_when_the_global_is_missing_throws_the_stable_global_unavailable_error()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState missingState = new();
		using (HostScope missingScope = new(missingState))
		{
			InstallCurrentTarget(missingScope.State);
			TargetMemoryAllocator allocator = new();
			EngineGlobalUnavailableException missing = Assert.Throws<EngineGlobalUnavailableException>(() =>
				allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))));
			Assert.Equal("TargetMemoryAllocate", missing.Operation);
			Assert.Equal(0, missingScope.State.Top);
		}
	}

	[Fact]
	public void Allocate_when_lua_raises_throws_the_stable_lua_error()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, "function allocateMemory() error('fixture allocation failure') end"u8);
		EngineLuaException lua = Assert.Throws<EngineLuaException>(() =>
			LuaTargetMemoryAllocationOperations.Instance.TryAllocate(
				new TargetAllocationRequest(new TargetAllocationSize(4096)), out _));
		Assert.Equal("TargetMemoryAllocate", lua.Operation);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Allocate_when_the_result_is_not_an_address_or_nil_throws_the_stable_marshalling_error()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, "function allocateMemory() return true end"u8);
		EngineMarshallingException malformed = Assert.Throws<EngineMarshallingException>(() =>
			LuaTargetMemoryAllocationOperations.Instance.TryAllocate(
				new TargetAllocationRequest(new TargetAllocationSize(4096)), out _));
		Assert.Equal("TargetMemoryAllocate", malformed.Operation);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Release_when_deallocation_returns_false_throws_the_stable_expected_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();

		EngineTest.Run(L, "function deAlloc() return false end"u8);
		EngineOperationFailedException expectedFailure = Assert.Throws<EngineOperationFailedException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))).Release());
		Assert.Equal("TargetMemoryDeallocate", expectedFailure.Operation);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void ReleaseWithOutcome_false_result_is_an_expected_failure_without_a_lua_error()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();
		EngineTest.Run(L, "function deAlloc() return false end"u8);
		AllocatedRegion region = allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096)));

		TargetMemoryOperationOutcome outcome = region.ReleaseWithOutcome();

		Assert.Equal(TargetMemoryOperationOutcomeKind.ExpectedFailure, outcome.Kind);
		Assert.Equal(EngineFailureKind.ExpectedOperationFailure, outcome.FailureKind);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
		Assert.True(region.IsDisposed);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Release_when_deallocation_lua_raises_throws_the_stable_lua_error()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();
		EngineTest.Run(L, "function deAlloc() error('fixture deallocation failure') end"u8);
		EngineLuaException lua = Assert.Throws<EngineLuaException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))).Release());
		Assert.Equal("TargetMemoryDeallocate", lua.Operation);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Release_when_deallocation_returns_a_nonboolean_throws_the_stable_marshalling_error()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallAllocationGlobals(L);
		TargetMemoryAllocator allocator = new();
		EngineTest.Run(L, "function deAlloc() return 1 end"u8);
		EngineMarshallingException malformed = Assert.Throws<EngineMarshallingException>(() =>
			allocator.Allocate(new TargetAllocationRequest(new TargetAllocationSize(4096))).Release());
		Assert.Equal("TargetMemoryDeallocate", malformed.Operation);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[InlineData(0UL, 4096)]
	[InlineData(0x7FF610000000UL, 0)]
	public void DeallocateWithOutcome_with_an_invalid_request_refuses_before_calling_the_Lua_boundary(ulong rawAddress,
		int rawSize)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  deallocation_call_count = 0
		                  function deAlloc()
		                    deallocation_call_count = deallocation_call_count + 1
		                    return true
		                  end
		                  """u8);

		TargetAllocationSize size = rawSize == 0 ? default : new TargetAllocationSize(rawSize);
		TargetMemoryOperationOutcome outcome =
			LuaTargetMemoryAllocationOperations.DeallocateWithOutcome(new Address(rawAddress), size);

		Assert.Equal(TargetMemoryOperationOutcomeKind.MarshallingFailure, outcome.Kind);
		Assert.Equal(EngineFailureKind.MarshallingFailure, outcome.FailureKind);
		AssertLuaInteger(L, "deallocation_call_count", 0);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Bound_allocate_with_no_selected_target_returns_an_identity_refusal_before_allocating()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  allocation_call_count = 0
		                  function getOpenedProcessID() return 0 end
		                  function allocateMemory()
		                    allocation_call_count = allocation_call_count + 1
		                    return 0x7FF610000000
		                  end
		                  """u8);
		ITargetBoundMemoryAllocationOperations operations = LuaTargetMemoryAllocationOperations.Instance;

		TargetMemoryAllocationOutcome outcome = operations.AllocateBoundWithOutcome(
			new TargetAllocationRequest(new TargetAllocationSize(4096)),
			out TargetProcessIncarnation incarnation, out TargetSelectionObservation observation);

		Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, observation.Status);
		Assert.Equal(default, incarnation);
		Assert.Equal(TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable, outcome.Operation.Kind);
		Assert.Equal(Address.Zero, outcome.Address);
		AssertLuaInteger(L, "allocation_call_count", 0);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Bound_deallocate_with_a_changed_target_returns_an_identity_mismatch_without_deallocating()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		EngineTest.Run(L, """
		                  deallocation_call_count = 0
		                  function deAlloc()
		                    deallocation_call_count = deallocation_call_count + 1
		                    return true
		                  end
		                  """u8);
		ITargetBoundMemoryAllocationOperations operations = LuaTargetMemoryAllocationOperations.Instance;
		TargetProcessIncarnation differentTarget = new(Environment.ProcessId + 1, 1001);

		TargetMemoryOperationOutcome outcome = operations.DeallocateBoundWithOutcome(differentTarget,
			new Address(0x7FF610000000),
			new TargetAllocationSize(4096), out TargetIdentityCheck targetCheck);

		Assert.Equal(TargetIdentityCheckKind.TargetChanged, targetCheck.Kind);
		Assert.Equal(TargetMemoryOperationOutcomeKind.TargetIdentityMismatch, outcome.Kind);
		Assert.Equal(EngineFailureKind.TargetIdentityMismatch, outcome.FailureKind);
		AssertLuaInteger(L, "deallocation_call_count", 0);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Bound_deallocate_with_an_invalid_request_returns_a_marshalling_failure_without_observing_a_target()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		ITargetBoundMemoryAllocationOperations operations = LuaTargetMemoryAllocationOperations.Instance;
		TargetProcessIncarnation expected = new(4101, 1001);

		TargetMemoryOperationOutcome outcome = operations.DeallocateBoundWithOutcome(expected, Address.Zero,
			new TargetAllocationSize(4096),
			out TargetIdentityCheck targetCheck);

		Assert.Equal(TargetIdentityCheckKind.Unspecified, targetCheck.Kind);
		Assert.Equal(TargetMemoryOperationOutcomeKind.MarshallingFailure, outcome.Kind);
		Assert.Equal(EngineFailureKind.MarshallingFailure, outcome.FailureKind);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Allocation_operations_while_detached_retain_the_normal_lifecycle_failure()
	{
		LuaRuntime.Detach();

		Assert.Throws<InvalidOperationException>(() => LuaTargetMemoryAllocationOperations.Instance.TryAllocate(
			new TargetAllocationRequest(new TargetAllocationSize(4096)), out _));
	}

	private static void InstallAllocationGlobals(LuaState state)
	{
		InstallCurrentTarget(state);
		EngineTest.Run(state, """
		                      function allocateMemory(...)
		                        allocation_argument_count = select('#', ...)
		                        allocation_size = select(1, ...)
		                        allocation_preferred = select(2, ...)
		                        allocation_preferred_is_nil = allocation_preferred == nil
		                        allocation_protection = select(3, ...)
		                        return 0x7FF610000000
		                      end
		                      function deAlloc(address, size)
		                        deallocation_address = address
		                        deallocation_size = size
		                        return true
		                      end
		                      """u8);
	}

	private static void InstallCurrentTarget(LuaState state)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes("function getOpenedProcessID() return " +
		                                             Environment.ProcessId + " end"));
		EngineTest.Run(state, FakeHost.LocalTargetBackendChunk);
	}

	private static void AssertLuaInteger(LuaState state, string name, long expected)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes("return " + name), 1);
		Assert.Equal(expected, EngineTest.ReadInteger(state, -1));
		state.Pop(1);
	}

	private static void AssertLuaBoolean(LuaState state, string name, bool expected)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes("return " + name), 1);
		Assert.Equal(expected, state.ToBoolean(-1));
		state.Pop(1);
	}
}
