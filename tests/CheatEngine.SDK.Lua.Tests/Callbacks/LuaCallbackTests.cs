using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Protected;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Tests.Callbacks;

/// <summary>
///     Managed functions called from Lua: registration, state through the upvalue, and the error channel (a failure
///     reported by a thunk is a catchable Lua error with the thunk's message; no managed exception ever escapes).
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class LuaCallbackTests
{
	[Fact]
	public void A_stateless_function_is_registered_and_called()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);

		Assert.True(L.TryPushFunction(Thunks.Add).IsOk);
		Assert.True(L.IsFunction(-1));
		Assert.True(L.TrySetGlobal("add"u8).IsOk);
		Assert.Equal(0, L.Top);

		LuaTest.Run(L, "return add(40, 2), add(-1, 1)"u8, 2);
		Assert.True(L.TryReadInteger(1, out long first));
		Assert.Equal(42, first);
		Assert.True(L.TryReadInteger(2, out long second));
		Assert.Equal(0, second);
	}

	[Fact]
	public void Generated_function_closure_retained_across_state_reset_is_rejected()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);

		Assert.True(LuaRuntime.TryPushGeneratedFunction(L, Thunks.Add).IsOk);
		Assert.True(L.TrySetGlobal("add"u8).IsOk);
		LuaTest.Run(L, "savedAdd = add"u8);

		using (LuaRuntime.BeginStateReset())
		{
		}

		LuaTest.Run(L, "local ok, err = pcall(savedAdd, 1, 2) return ok, err"u8, 2);
		Assert.False(L.ToBoolean(1));
		Assert.Contains("Lua function registration has expired", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q14")]
	public void A_failure_reported_by_the_thunk_is_a_catchable_lua_error_with_the_message()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Register(L, Thunks.Add, "add"u8);

		LuaTest.Run(L, "local ok, err = pcall(add, 'x', 2)\nreturn ok, err"u8, 2);

		Assert.True(BooleanMarshaller.TryRead(L, 1, out bool ok));
		Assert.False(ok);
		string message = LuaTest.ReadString(L, 2);
		Assert.Contains("add expects two numbers", message, StringComparison.Ordinal);
	}

	[Fact]
	public void An_unprotected_call_of_a_failing_thunk_fails_the_enclosing_protected_call()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Register(L, Thunks.Add, "add"u8);

		LuaStatus status = L.TryExecute("local v = add(nil, nil)\nreturn v"u8, 1, "=script"u8);

		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal(1, L.Top);
		string message = LuaError.FromStack(L, status).Message;
		Assert.Contains("add expects two numbers", message, StringComparison.Ordinal);
		// error(message, 2) blames the script line that called the function, not the SDK's wrapper.
		Assert.StartsWith("script:1:", message, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q14")]
	public void A_managed_exception_inside_a_thunk_never_escapes_and_becomes_a_lua_error()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Register(L, Thunks.Throw, "throwing"u8);

		LuaTest.Run(L, "local ok, err = pcall(throwing)\nreturn ok, err"u8, 2);

		Assert.True(BooleanMarshaller.TryRead(L, 1, out bool ok));
		Assert.False(ok);
		string message = LuaTest.ReadString(L, 2);
		Assert.Contains("System.InvalidOperationException", message, StringComparison.Ordinal);
		Assert.Contains("managed boom", message, StringComparison.Ordinal);
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void An_unchecked_function_hands_the_sentinel_and_message_to_the_caller_as_results()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		L.PushUncheckedFunction(Thunks.Add);
		Assert.True(L.TrySetGlobal("rawAdd"u8).IsOk);

		LuaTest.Run(L, "local a, b = rawAdd('x', 1)\nreturn type(a), b, rawAdd(1, 2)"u8, 3);

		Assert.Equal("userdata", LuaTest.ReadString(L, 1));
		Assert.Equal("add expects two numbers", LuaTest.ReadString(L, 2));
		Assert.True(L.TryReadInteger(3, out long sum));
		Assert.Equal(3, sum);
	}

	[Fact]
	public void Echo_shows_that_a_thunk_sees_exactly_the_caller_arguments_and_returns_exactly_its_results()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Register(L, Thunks.Echo, "echo"u8);

		LuaTest.Run(L, "return select('#', echo()), select('#', echo(nil)), echo(1, 'two', nil)"u8, 5);

		Assert.True(L.TryReadInteger(1, out long none));
		Assert.Equal(0, none);
		Assert.True(L.TryReadInteger(2, out long oneNil));
		Assert.Equal(1, oneNil);
		Assert.True(L.TryReadInteger(3, out long one));
		Assert.Equal(1, one);
		Assert.Equal("two", LuaTest.ReadString(L, 4));
		Assert.True(L.IsNil(5));
	}

	[Fact]
	public void A_callback_carries_its_state_through_the_upvalue()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter counter = new();

		LuaStatus status = LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback);

		Assert.True(status.IsOk);
		Assert.NotNull(callback);
		Assert.Equal(0, L.Top);
		Assert.Same(counter, callback.State);
		Assert.Same(counter, callback.StateObject);
		Assert.True(callback.IsCurrent);
		Assert.False(callback.IsReleased);
		Assert.Equal(1, LuaCallbackRegistry.Count);

		Assert.True(callback.TryRegister(L, "count"u8).IsOk);
		LuaTest.Run(L, "return count(), count(), count()"u8, 3);
		Assert.Equal(3, counter.Value);
		Assert.Equal(state.Pointer, counter.LastState);
		Assert.True(L.TryReadInteger(3, out long third));
		Assert.Equal(3, third);

		callback.Release(L);
		Assert.Equal(0, LuaCallbackRegistry.Count);
	}

	[Fact]
	public unsafe void Protected_raw_setters_accept_a_table_closure_upvalue()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);

		L.CreateTable();
		lua_pushcclosure(L.Pointer, Thunks.SetUpvalueTable.Pointer, 1);
		LuaStatus status = L.TryCall(0, 1);

		Assert.True(status.IsOk, status.IsOk ? "" : LuaError.FromStack(L, status).Message);
		L.PushString("name"u8);
		Assert.Equal(LuaType.Number, L.RawGet(1));
		Assert.True(L.TryReadInteger(-1, out long named));
		Assert.Equal(1, named);
		L.Pop(1);

		Assert.Equal(LuaType.Number, L.RawGetIndex(1, 2));
		Assert.True(L.TryReadInteger(-1, out long indexed));
		Assert.Equal(2, indexed);
		L.Pop(1);

		Assert.Equal(LuaType.Number, L.RawGetPointer(1, 0x77));
		Assert.True(L.TryReadInteger(-1, out long pointed));
		Assert.Equal(3, pointed);
		L.Pop(1);
	}

	[Fact]
	public void Two_callbacks_of_the_same_thunk_have_independent_state()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter a = new();
		Counter b = new()
		{
			Value = 100
		};
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, a, out LuaCallback<Counter>? callbackA).IsOk);
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, b, out LuaCallback<Counter>? callbackB).IsOk);
		Assert.True(callbackA!.TryRegister(L, "countA"u8).IsOk);
		Assert.True(callbackB!.TryRegister(L, "countB"u8).IsOk);

		LuaTest.Run(L, "countA(); countA(); return countB()"u8, 1);

		Assert.Equal(2, a.Value);
		Assert.Equal(101, b.Value);
		callbackA.Release(L);
		callbackB.Release(L);
	}

	[Fact]
	[Trait("Qualification", "Q15")]
	public void Releasing_a_callback_neutralizes_the_closure_a_script_kept()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.True(callback!.TryRegister(L, "count"u8).IsOk);
		LuaTest.Run(L, "kept = count"u8);

		callback.Release(L);
		callback.Release(L);

		Assert.True(callback.IsReleased);
		Assert.False(callback.IsCurrent);
		Assert.Null(callback.State);
		Assert.False(callback.TryPush(L));

		// A Try* member reports, it does not throw: a runtime-error status with the message on the stack, like any
		// other failed protected operation.
		LuaStatus status = callback.TryRegister(L, "again"u8);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal(1, L.Top);
		Assert.Contains("released", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		L.Pop(1);

		// The function a script kept still exists, but its upvalue is gone: it reports instead of touching freed memory.
		LuaTest.Run(L, "local ok, err = pcall(kept)\nreturn ok, err"u8, 2);
		Assert.False(L.ToBoolean(1));
		Assert.Contains("callback released", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
		Assert.Equal(0, counter.Value);
	}

	[Fact]
	[Trait("Qualification", "Q15")]
	public void Detach_neutralizes_every_callback_the_plugin_forgot()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter counter = new();
		LuaCallback<Counter>? callback;
		using (new RuntimeScope(state))
		{
			Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out callback).IsOk);
			Assert.True(callback!.TryRegister(L, "count"u8).IsOk);
			LuaTest.Run(L, "count()"u8);
			Assert.Equal(1, LuaCallbackRegistry.Count);
		}

		Assert.True(callback.IsReleased);
		Assert.Equal(0, LuaCallbackRegistry.Count);
		Assert.Null(callback.StateObject);

		LuaTest.Run(L, "local ok, err = pcall(count)\nreturn ok, err"u8, 2);
		Assert.False(L.ToBoolean(1));
		Assert.Contains("callback released", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
		Assert.Equal(1, counter.Value);
	}

	[Fact]
	[Trait("Qualification", "Q17")]
	public void BeginStateReset_neutralizes_every_callback_before_the_state_is_replaced()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.True(callback!.TryRegister(L, "count"u8).IsOk);
		LuaTest.Run(L, "kept = count"u8);
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;

		using (LuaRuntime.BeginStateReset())
		{
		}

		LuaStateIdentity after = LuaRuntime.CurrentStateIdentity;
		Assert.Equal(before.AttachEpoch, after.AttachEpoch);
		Assert.Equal(before.StateGeneration + 1, after.StateGeneration);
		Assert.True(callback.IsReleased);
		Assert.False(callback.IsCurrent);
		Assert.Null(callback.StateObject);
		Assert.Equal(0, LuaCallbackRegistry.Count);

		// The fixture leaves the old state alive so this verifies preparation order: the old closure was neutralized
		// before a future host reset can replace that state.
		LuaTest.Run(L, "local ok, err = pcall(kept)\nreturn ok, err"u8, 2);
		Assert.False(L.ToBoolean(1));
		Assert.Contains("callback released", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
		Assert.Equal(0, counter.Value);
	}

	[Fact]
	public void Dispose_while_detached_abandons_the_handle_instead_of_freeing_it_behind_the_closure()
	{
		LuaTest.RequireNativeLua();
		LuaRuntime.Detach();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.True(callback!.TryRegister(L, "count"u8).IsOk);

		callback.Dispose();

		Assert.True(callback.IsReleased);
		Assert.Equal(0, LuaCallbackRegistry.Count);
		// No state could be acquired, so the closure was not neutralized and the state object deliberately stays alive.
		LuaTest.Run(L, "return count()"u8, 1);
		Assert.Equal(1, counter.Value);
	}

	[Fact]
	public async Task Detach_closes_admission_and_drains_callback_creation_before_registry_publication()
	{
		LuaTest.RequireNativeLua();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using ManualResetEventSlim creationPaused = new(false);
		using ManualResetEventSlim allowPublication = new(false);
		using ManualResetEventSlim admissionClosed = new(false);
		Counter counter = new();

		LuaCallback.SetBeforeRegistryAddForTesting(() =>
		{
			creationPaused.Set();
			if (!allowPublication.Wait(TimeSpan.FromSeconds(5), cancellationToken))
			{
				throw new TimeoutException("The callback-publication barrier timed out.");
			}
		});
		LuaRuntime.OperationAdmissionClosedForTesting = admissionClosed.Set;

		try
		{
			Task<LuaCallback<Counter>?> creation = Task.Factory.StartNew(() =>
			{
				LuaStatus status = LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback);
				Assert.True(status.IsOk);
				return callback;
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			Assert.True(creationPaused.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"Callback creation did not reach its pre-registry barrier.");

			Task detach = Task.Factory.StartNew(LuaRuntime.Detach, cancellationToken, TaskCreationOptions.LongRunning,
				TaskScheduler.Default);
			Assert.True(admissionClosed.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"Detach did not close Lua operation admission.");
			Assert.False(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation rejected));
			rejected.Dispose();
			Assert.False(detach.IsCompleted,
				"Detach completed while an admitted callback creation had not published to the registry.");

			allowPublication.Set();
			LuaCallback<Counter>? callback = await creation.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
			await detach.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

			Assert.NotNull(callback);
			Assert.True(callback.IsReleased);
			Assert.Null(callback.StateObject);
			Assert.Equal(0, LuaCallbackRegistry.Count);
		}
		finally
		{
			LuaCallback.SetBeforeRegistryAddForTesting(null);
			LuaRuntime.OperationAdmissionClosedForTesting = null;
			allowPublication.Set();
		}
	}

	[Fact]
	public async Task Dispose_after_detach_closes_admission_defers_to_callback_neutralization()
	{
		LuaTest.RequireNativeLua();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using ManualResetEventSlim admissionClosed = new(false);
		using ManualResetEventSlim allowCleanup = new(false);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.NotNull(callback);
		Assert.True(callback.TryRegister(L, "count"u8).IsOk);

		LuaRuntime.OperationAdmissionClosedForTesting = () =>
		{
			admissionClosed.Set();
			if (!allowCleanup.Wait(TimeSpan.FromSeconds(5), cancellationToken))
			{
				throw new TimeoutException("The detach-cleanup barrier timed out.");
			}
		};

		try
		{
			Task detach = Task.Factory.StartNew(LuaRuntime.Detach, cancellationToken, TaskCreationOptions.LongRunning,
				TaskScheduler.Default);
			Assert.True(admissionClosed.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"Detach did not close operation admission.");

			callback.Dispose();

			// Detach has the only state that may clear the Lua upvalue. Dispose therefore leaves the callback linked
			// until that cleanup runs instead of abandoning its handle and losing the closure from the registry.
			Assert.False(callback.IsReleased);
			Assert.Equal(1, LuaCallbackRegistry.Count);

			allowCleanup.Set();
			await detach.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

			Assert.True(callback.IsReleased);
			Assert.Null(callback.StateObject);
			Assert.Equal(0, LuaCallbackRegistry.Count);

			LuaTest.Run(L, "local ok, err = pcall(count) return ok, err"u8, 2);
			Assert.False(L.ToBoolean(1));
			Assert.Contains("callback released", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
			Assert.Equal(0, counter.Value);
		}
		finally
		{
			LuaRuntime.OperationAdmissionClosedForTesting = null;
			allowCleanup.Set();
		}
	}

	[Fact]
	public async Task Dispose_observing_closed_admission_remains_linked_when_detach_failure_reopens_it()
	{
		LuaTest.RequireNativeLua();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using DisposeAdmissionFailureRace race = new(cancellationToken);
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, new Counter(), out LuaCallback<Counter>? first).IsOk);
		Assert.True(first!.TryRegister(L, "first"u8).IsOk);
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, new Counter(), out LuaCallback<Counter>? second).IsOk);
		Assert.True(second!.TryRegister(L, "second"u8).IsOk);

		Task detach = Task.Factory.StartNew(LuaRuntime.Detach, cancellationToken, TaskCreationOptions.LongRunning,
			TaskScheduler.Default);
		Assert.True(race.AdmissionClosed.Wait(TimeSpan.FromSeconds(5), cancellationToken),
			"Detach did not close operation admission.");

		Task dispose = Task.Factory.StartNew(first.Dispose, cancellationToken, TaskCreationOptions.LongRunning,
			TaskScheduler.Default);
		Assert.True(race.DisposeObservedRefusal.Wait(TimeSpan.FromSeconds(5), cancellationToken),
			"Dispose did not observe the closed admission gate.");

		race.AllowDetachCleanup.Set();
		await Assert.ThrowsAsync<InvalidOperationException>(() => detach.WaitAsync(TimeSpan.FromSeconds(5),
			cancellationToken));
		Assert.True(second.IsReleased);
		Assert.False(first.IsReleased);
		Assert.True(LuaRuntime.IsAttached);
		Assert.Equal(1, LuaCallbackRegistry.Count);

		race.AllowDisposeToReturn.Set();
		await dispose.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

		Assert.False(first.IsReleased);
		Assert.Equal(1, LuaCallbackRegistry.Count);

		DisposeAdmissionFailureRace.DisableFailureSeams();
		LuaRuntime.Detach();

		Assert.True(first.IsReleased);
		Assert.Equal(0, LuaCallbackRegistry.Count);
		LuaTest.Run(L, "local ok, err = pcall(first) return ok, err"u8, 2);
		Assert.False(L.ToBoolean(1));
		Assert.Contains("callback released", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q08")]
	public void Detach_cleanup_failure_keeps_remaining_callbacks_and_allows_a_retry()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Counter firstCounter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, firstCounter, out LuaCallback<Counter>? first).IsOk);
		Assert.True(first!.TryRegister(L, "first"u8).IsOk);
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, new Counter(), out LuaCallback<Counter>? second).IsOk);
		Assert.True(second!.TryRegister(L, "second"u8).IsOk);
		int releases = 0;
		LuaCallbackRegistry.AfterReleaseForTesting = () =>
		{
			if (++releases == 1)
			{
				throw new InvalidOperationException("deterministic callback cleanup failure");
			}
		};

		try
		{
			Assert.Throws<InvalidOperationException>(LuaRuntime.Detach);

			// The newly created callback was the registry head and was released before the injected error. Its older
			// sibling remains linked; the old host binding is still published and normal work is admitted again.
			Assert.True(second.IsReleased);
			Assert.False(first.IsReleased);
			Assert.Equal(1, LuaCallbackRegistry.Count);
			Assert.True(LuaRuntime.IsAttached);
			Assert.Equal(scope.Binding, LuaRuntime.CurrentBinding);
			Assert.True(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation));
			operation.Dispose();

			LuaTest.Run(L, "return first()"u8, 1);
			Assert.Equal(1, firstCounter.Value);
			L.Pop(1);

			LuaCallbackRegistry.AfterReleaseForTesting = null;
			LuaRuntime.Detach();

			Assert.True(first.IsReleased);
			Assert.Equal(0, LuaCallbackRegistry.Count);
			LuaTest.Run(L, "local ok, err = pcall(first) return ok, err"u8, 2);
			Assert.False(L.ToBoolean(1));
			Assert.Contains("callback released", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
		}
		finally
		{
			LuaCallbackRegistry.AfterReleaseForTesting = null;
		}
	}

	[Fact]
	public unsafe void Failed_attach_replacement_reopens_admission_for_the_previous_binding()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state, false);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.NotNull(callback);
		LuaHostBinding replacement = HostDouble.CreateBinding(state.L);
		LuaCallbackRegistry.AfterReleaseForTesting = static () =>
			throw new InvalidOperationException("deterministic callback cleanup failure");

		try
		{
			Assert.Throws<InvalidOperationException>(() => LuaRuntime.Attach(replacement));

			Assert.True(callback.IsReleased);
			Assert.True(LuaRuntime.IsAttached);
			Assert.Equal(scope.Binding, LuaRuntime.CurrentBinding);
			Assert.True(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation));
			operation.Dispose();
		}
		finally
		{
			LuaCallbackRegistry.AfterReleaseForTesting = null;
		}
	}

	[Fact]
	public void Dispose_with_the_runtime_attached_releases_like_release()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.True(callback!.TryRegister(L, "count"u8).IsOk);

		callback.Dispose();

		Assert.Null(callback.StateObject);
		LuaTest.Run(L, "return pcall(count)"u8, 2);
		Assert.False(L.ToBoolean(1));
	}

	[Fact]
	public void A_callback_created_in_an_earlier_epoch_is_stale_but_still_safe()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter counter = new();
		LuaCallback<Counter>? callback;
		using (new RuntimeScope(state))
		{
			Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out callback).IsOk);
		}

		// Detach released it already; a fresh epoch makes any surviving reference stale.
		using (new RuntimeScope(state))
		{
			Assert.False(callback!.IsCurrent);
			Assert.False(callback.TryPush(L));
			Assert.Equal(0, L.Top);
		}
	}

	[Fact]
	public void TryCreate_validates_its_arguments()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);

		Assert.Throws<ArgumentException>(() => LuaCallback.TryCreate(L, default, new Counter(), out _));
		Assert.Throws<ArgumentNullException>(() => LuaCallback.TryCreate<Counter>(L, Thunks.Count, null!, out _));
		Assert.Throws<ArgumentException>(() => L.TryPushFunction(default));
		Assert.Throws<ArgumentException>(() => L.PushUncheckedFunction(default));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void A_string_argument_reaches_a_string_parameter_and_a_wrong_one_is_named_like_lua_does()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Register(L, Thunks.Greet, "greet"u8);

		LuaTest.Run(L, "local ok, err = pcall(greet, 42)\nreturn greet('world'), ok, err, select(2, pcall(greet))"u8,
			4);

		Assert.Equal("hello, world", LuaTest.ReadString(L, 1));
		Assert.False(L.ToBoolean(2));
		Assert.EndsWith("bad argument #1 (string expected, got number)", LuaTest.ReadString(L, 3),
			StringComparison.Ordinal);
		Assert.EndsWith("bad argument #1 (string expected, got no value)", LuaTest.ReadString(L, 4),
			StringComparison.Ordinal);
	}

	[Fact]
	public void FailBadArgument_allocates_nothing_on_the_managed_side()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Register(L, Thunks.Greet, "greet"u8);
		LuaTest.Run(L, "return function() local ok = pcall(greet, 42) return ok end"u8, 1);
		int failures = 0;

		AllocationGate.AssertZero(
			() =>
			{
				L.PushValue(1);
				if (!L.TryCall(0, 1).IsOk || L.ToBoolean(-1))
				{
					throw new InvalidOperationException("greet(42) should fail inside pcall");
				}

				failures++;
				L.Pop(1);
			},
			200);

		Assert.True(failures > 0);
	}

	[Fact]
	public void The_error_channel_survives_compacting_garbage_collections()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Register(L, Thunks.Add, "add"u8);
		IntPtr sentinelBefore = LuaHelpers.SentinelAddress;

		// The registry keys and the sentinel are addresses compared for the life of the load context. Static
		// value-type storage may move under a compacting collection; the block the helpers use must not.
		for (int round = 0; round < 3; round++)
		{
			for (int i = 0; i < 10_000; i++)
			{
				_ = new byte[64];
			}

			GC.Collect(2, GCCollectionMode.Forced, true, true);
			GC.WaitForPendingFinalizers();
		}

		Assert.Equal(sentinelBefore, LuaHelpers.SentinelAddress);
		LuaTest.Run(L, "local ok, err = pcall(add, 'x', 2)\nreturn ok, err, add(1, 2)"u8, 3);
		Assert.False(L.ToBoolean(1));
		Assert.Contains("add expects two numbers", LuaTest.ReadString(L, 2), StringComparison.Ordinal);
		Assert.True(L.TryReadInteger(3, out long sum));
		Assert.Equal(3, sum);

		// Still the same helper set: a second registration does not install a second chunk.
		Assert.True(L.TryGetGlobal("add"u8).IsOk);
		Assert.True(L.IsFunction(-1));
	}

	[Fact]
	public void The_first_protected_operation_of_a_state_works_from_a_thunk_that_used_most_of_its_free_slots()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		// The global is set raw so that this state has no helpers installed when the thunk runs.
		L.PushGlobalTable();
		L.PushString("x"u8);
		L.PushInteger(1234);
		Assert.True(L.TryRawSet(-3));
		L.Pop(1);

		// The thunk is pushed bare (no wrapper, no helper install) and runs the install itself from a deep stack:
		// the install asks lua_checkstack for the room lua_pcallk's result contract requires.
		// Two results are kept so that a thunk failure (sentinel, message) is readable: unchecked functions have no wrapper.
		L.PushUncheckedFunction(Thunks.DeepStackFirstProtected);
		LuaStatus status = L.TryCall(0, 2);

		Assert.True(status.IsOk, status.IsOk ? "" : LuaError.FromStack(L, status).Message);
		Assert.True(L.TypeOf(1) == LuaType.Number, L.TryReadString(2, out string? failure) ? failure : "no value");
		Assert.True(L.TryReadInteger(1, out long value));
		Assert.Equal(1234, value);
		Assert.Equal(2, L.Top);
	}

	[Fact]
	public void Failure_of_the_thunk_inside_a_coroutine_uses_the_coroutine_state()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		Counter counter = new();
		Assert.True(LuaCallback.TryCreate(L, Thunks.Count, counter, out LuaCallback<Counter>? callback).IsOk);
		Assert.True(callback!.TryRegister(L, "count"u8).IsOk);

		LuaTest.Run(L, "local co = coroutine.wrap(function() return count() end)\nreturn co()"u8, 1);

		Assert.Equal(1, counter.Value);
		Assert.NotEqual(state.Pointer, counter.LastState);
		callback.Release(L);
	}

	private static void Register(LuaState L, LuaNativeFunction thunk, ReadOnlySpan<byte> name)
	{
		Assert.True(L.TryPushFunction(thunk).IsOk);
		Assert.True(L.TrySetGlobal(name).IsOk);
	}

	private sealed class DisposeAdmissionFailureRace : IDisposable
	{
		private readonly CancellationToken _cancellationToken;

		public DisposeAdmissionFailureRace(CancellationToken cancellationToken)
		{
			_cancellationToken = cancellationToken;
			LuaRuntime.OperationAdmissionClosedForTesting = OnAdmissionClosed;
			LuaCallback.SetDisposeAdmissionRefusedForTesting(OnDisposeAdmissionRefused);
			LuaCallbackRegistry.AfterReleaseForTesting = ThrowCleanupFailure;
		}

		public ManualResetEventSlim AdmissionClosed
		{
			get;
		} = new(false);

		public ManualResetEventSlim AllowDetachCleanup
		{
			get;
		} = new(false);

		public ManualResetEventSlim DisposeObservedRefusal
		{
			get;
		} = new(false);

		public ManualResetEventSlim AllowDisposeToReturn
		{
			get;
		} = new(false);

		public void Dispose()
		{
			DisableFailureSeams();
			AllowDetachCleanup.Set();
			AllowDisposeToReturn.Set();
			AdmissionClosed.Dispose();
			AllowDetachCleanup.Dispose();
			DisposeObservedRefusal.Dispose();
			AllowDisposeToReturn.Dispose();
		}

		public static void DisableFailureSeams()
		{
			LuaRuntime.OperationAdmissionClosedForTesting = null;
			LuaCallback.SetDisposeAdmissionRefusedForTesting(null);
			LuaCallbackRegistry.AfterReleaseForTesting = null;
		}

		private void OnAdmissionClosed()
		{
			AdmissionClosed.Set();
			if (!AllowDetachCleanup.Wait(TimeSpan.FromSeconds(5), _cancellationToken))
			{
				throw new TimeoutException("The detach-cleanup barrier timed out.");
			}
		}

		private void OnDisposeAdmissionRefused()
		{
			DisposeObservedRefusal.Set();
			if (!AllowDisposeToReturn.Wait(TimeSpan.FromSeconds(5), _cancellationToken))
			{
				throw new TimeoutException("The callback-disposal barrier timed out.");
			}
		}

		private static void ThrowCleanupFailure()
		{
			throw new InvalidOperationException("deterministic callback cleanup failure");
		}
	}
}
