using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>
///     Attach and detach with an <c>[UnmanagedCallersOnly]</c> host double, and what the runtime hands out in
///     between.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class LuaRuntimeTests
{
	[Fact]
	public void Attach_publishes_the_binding_and_advances_the_attach_epoch_once()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaRuntime.Detach();
		LuaStateIdentity identity = LuaRuntime.CurrentStateIdentity;

		using (RuntimeScope scope = new(state))
		{
			Assert.True(LuaRuntime.IsAttached);
			Assert.Equal(identity.AttachEpoch + 1, LuaRuntime.Epoch);
			Assert.Equal(identity.StateGeneration, LuaRuntime.StateGeneration);
			Assert.Equal(LuaRuntime.Epoch, LuaRuntime.CurrentStateIdentity.AttachEpoch);
			Assert.Equal(LuaRuntime.StateGeneration, LuaRuntime.CurrentStateIdentity.StateGeneration);
			Assert.Equal(scope.Binding, LuaRuntime.CurrentBinding);
			Assert.Equal(HostDouble.ProviderAddress, LuaRuntime.CurrentBinding.StateProvider);
			Assert.Equal(HostDouble.PusherAddress, LuaRuntime.CurrentBinding.HostObjectPusher);
			Assert.True(LuaRuntime.IsMainThread);
		}

		Assert.False(LuaRuntime.IsAttached);
		Assert.Equal(identity.AttachEpoch + 1, LuaRuntime.Epoch);
		Assert.False(LuaRuntime.IsMainThread);
	}

	[Fact]
	[Trait("Qualification", "Q17")]
	public void BeginStateReset_advances_only_the_state_generation_while_the_host_remains_attached()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;

		using (LuaRuntime.BeginStateReset())
		{
		}

		LuaStateIdentity after = LuaRuntime.CurrentStateIdentity;
		Assert.True(LuaRuntime.IsAttached);
		Assert.Equal(scope.Binding, LuaRuntime.CurrentBinding);
		Assert.Equal(before.AttachEpoch, after.AttachEpoch);
		Assert.Equal(before.StateGeneration + 1, after.StateGeneration);
		Assert.Equal(before.AttachEpoch, LuaRuntime.Epoch);
		Assert.Equal(after.StateGeneration, LuaRuntime.StateGeneration);
	}

	[Fact]
	public void AcquireOperation_calls_the_provider_once_per_operation_and_returns_its_state()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);

		using LuaRuntimeOperation firstOperation = LuaRuntime.AcquireOperation();
		using LuaRuntimeOperation secondOperation = LuaRuntime.AcquireOperation();
		Assert.True(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation thirdOperation));
		using (thirdOperation)
		{
			LuaState first = firstOperation.State;
			LuaState second = secondOperation.State;
			LuaState third = thirdOperation.State;

			Assert.Equal(state.Pointer, first.Handle);
			Assert.Equal(first, second);
			Assert.Equal(first, third);
			Assert.Equal(3, HostDouble.ProviderCalls);

			first.PushInteger(11);
			Assert.Equal(1, LuaTest.View(state).Top);
		}
	}

	[Fact]
	public unsafe void A_provider_that_returns_no_state_is_reported()
	{
		LuaTest.RequireNativeLua();
		LuaRuntime.Detach();
		LuaHostBinding binding = HostDouble.CreateBinding(null);
		LuaRuntime.Attach(in binding);
		try
		{
			Assert.False(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation));
			operation.Dispose();
			InvalidOperationException exception =
				Assert.Throws<InvalidOperationException>(() => LuaRuntime.AcquireOperation());
			Assert.Contains("no Lua state", exception.Message, StringComparison.Ordinal);

			// A failed provider acquisition must return its admission before a transition attempts to drain it.
			using (LuaRuntime.BeginStateReset())
			{
			}
		}
		finally
		{
			LuaRuntime.Detach();
		}
	}

	[Fact]
	public void IsMainThread_is_false_on_another_thread()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);
		bool onMain = LuaRuntime.IsMainThread;
		bool onWorker = true;

		Thread worker = new(() => onWorker = LuaRuntime.IsMainThread);
		worker.Start();
		worker.Join();

		Assert.True(onMain);
		Assert.False(onWorker);
	}

	[Fact]
	public void PushHostObject_uses_the_protected_bridge_to_call_the_pusher_with_the_object_pointer()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState L = operation.State;

		LuaRuntime.PushHostObject(L, 0xBEEF);

		Assert.Equal(1, HostDouble.PusherCalls);
		Assert.Equal(0xBEEF, HostDouble.LastPushedObject);
		Assert.Equal(1, L.Top);
		Assert.True(L.IsLightUserdata(-1));
		Assert.Equal(0xBEEF, L.ToUserdata(-1));
	}

	[Fact]
	public void PushHostObject_without_a_pusher_throws_instead_of_jumping_to_zero()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state, false);
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState L = operation.State;

		Assert.Equal(0, scope.Binding.HostObjectPusher);
		Assert.Throws<InvalidOperationException>(() => LuaRuntime.PushHostObject(L, 1));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public unsafe void Attach_while_attached_replaces_the_binding_and_advances_the_epoch_again()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaRuntime.Detach();
		int epoch = LuaRuntime.Epoch;
		LuaHostBinding first = HostDouble.CreateBinding(state.L, false);
		LuaRuntime.Attach(in first);
		try
		{
			LuaHostBinding second = HostDouble.CreateBinding(state.L);
			LuaRuntime.Attach(in second);

			Assert.Equal(epoch + 2, LuaRuntime.Epoch);
			Assert.Equal(second, LuaRuntime.CurrentBinding);
			Assert.NotEqual(first, second);
		}
		finally
		{
			LuaRuntime.Detach();
		}
	}

	[Fact]
	public async Task BeginStateReset_rejects_new_operations_and_waits_for_an_admitted_operation_to_leave()
	{
		LuaTest.RequireNativeLua();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);
		using ManualResetEventSlim workerAdmitted = new(false);
		using ManualResetEventSlim releaseWorker = new(false);
		using ManualResetEventSlim admissionClosed = new(false);
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;
		LuaRuntime.OperationAdmissionClosedForTesting = admissionClosed.Set;

		try
		{
			Task worker = Task.Factory.StartNew(() =>
			{
				using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
				workerAdmitted.Set();
				if (!releaseWorker.Wait(TimeSpan.FromSeconds(5), cancellationToken))
				{
					throw new TimeoutException("The operation-lease barrier timed out.");
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			Assert.True(workerAdmitted.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"The worker did not acquire a Lua operation admission.");

			Task reset = Task.Factory.StartNew(() =>
			{
				using LuaStateResetTransition transition = LuaRuntime.BeginStateReset();
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			Assert.True(admissionClosed.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"The reset did not close Lua operation admission.");
			Assert.False(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation rejected));
			rejected.Dispose();
			Assert.False(reset.IsCompleted, "Reset completed before the admitted operation released its lease.");

			releaseWorker.Set();
			await worker.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
			await reset.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

			LuaStateIdentity after = LuaRuntime.CurrentStateIdentity;
			Assert.Equal(before.AttachEpoch, after.AttachEpoch);
			Assert.Equal(before.StateGeneration + 1, after.StateGeneration);
			Assert.True(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation next));
			next.Dispose();
		}
		finally
		{
			LuaRuntime.OperationAdmissionClosedForTesting = null;
			releaseWorker.Set();
		}
	}

	[Fact]
	public void AcquireOperation_with_a_host_supplied_state_admits_without_recalling_the_provider()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);

		using LuaRuntimeOperation outer = LuaRuntime.AcquireOperation();
		int callsBeforeSuppliedLease = HostDouble.ProviderCalls;
		using LuaRuntimeOperation supplied = LuaRuntime.AcquireOperation(outer.State);

		Assert.Equal(outer.State, supplied.State);
		Assert.Equal(callsBeforeSuppliedLease, HostDouble.ProviderCalls);
	}

	[Fact]
	public void BeginStateReset_rejects_reentrancy_from_an_admitted_operation()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(StartAndCompleteStateReset);

		Assert.Contains("cannot start", exception.Message, StringComparison.Ordinal);
		Assert.Equal(before, LuaRuntime.CurrentStateIdentity);
	}

	[Fact]
	public async Task Detach_rejects_a_nested_transition_before_waiting_for_another_transition_lock()
	{
		LuaTest.RequireNativeLua();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using NativeLuaState state = new(false);
		using RuntimeScope scope = new(state);
		using ManualResetEventSlim admissionClosed = new(false);
		LuaRuntime.OperationAdmissionClosedForTesting = admissionClosed.Set;
		Task? competingDetach = null;

		try
		{
			using (LuaRuntime.AcquireOperation())
			{
				competingDetach = Task.Factory.StartNew(LuaRuntime.Detach, cancellationToken,
					TaskCreationOptions.LongRunning, TaskScheduler.Default);
				Assert.True(admissionClosed.Wait(TimeSpan.FromSeconds(5), cancellationToken),
					"The competing detach did not close operation admission.");

				InvalidOperationException exception = Assert.Throws<InvalidOperationException>(LuaRuntime.Detach);

				Assert.Contains("cannot start", exception.Message, StringComparison.Ordinal);
				Assert.False(competingDetach.IsCompleted,
					"The competing detach completed while the admitted operation was still active.");
			}

			await competingDetach!.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
		}
		finally
		{
			LuaRuntime.OperationAdmissionClosedForTesting = null;
		}
	}

	private static void StartAndCompleteStateReset()
	{
		using LuaStateResetTransition transition = LuaRuntime.BeginStateReset();
	}
}
