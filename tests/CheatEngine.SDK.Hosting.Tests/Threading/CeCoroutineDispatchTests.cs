using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Threading;

/// <summary>Models CE's rooted thread states and re-entry of the waiting worker coroutine.</summary>
public sealed unsafe class CeCoroutineDispatchTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait("Category", "NativeLua")]
	public void Repeated_dispatch_reenters_the_worker_coroutine_and_restores_both_stacks(bool throwInWork)
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState root = new();
		using ThreadStates states = new(root);
		using HostSimulator host = new();
		HostingTest.Enable(host, root);
		FakeExports.StateProviderForTests = states.GetState;
		nint mainState = states.GetState();
		int mainThread = Environment.CurrentManagedThreadId;
		using SynchronizeStandIn synchronize = new(new LuaState(mainState));
		const int repetitions = 32;
		ConcurrentQueue<Observation> observations = new();
		Task worker = Task.Factory.StartNew(() =>
		{
			nint workerState = states.GetState();
			for (int index = 0; index < repetitions && !synchronize.IsStopping; index++)
			{
				int before = LuaApi.lua_gettop((lua_State*) workerState);
				nint callbackState = 0;
				int callbackThread = 0;
				Exception? failure = Record.Exception(() => MainThread.Invoke(_ =>
				{
					callbackThread = Environment.CurrentManagedThreadId;
					using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
					callbackState = (nint) operation.State.Pointer;
					using LuaFrame frame = new(operation.State);
					operation.State.PushInteger(123);
					if (throwInWork)
					{
						throw new NotSupportedException("owned callback failure");
					}
				}, 0));
				observations.Enqueue(new Observation(before, LuaApi.lua_gettop((lua_State*) workerState),
					workerState, callbackState, callbackThread, failure));
			}
		}, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

		try
		{
			for (int index = 0; index < repetitions; index++)
			{
				Assert.True(synchronize.PumpOne(TimeSpan.FromSeconds(5)), "Worker did not queue the expected dispatch.");
			}
		}
		finally
		{
			synchronize.StopAndDrain();
			Join(worker);
		}
		Assert.Equal(repetitions, observations.Count);
		Assert.Equal(repetitions, synchronize.PumpedCalls);
		VerifyObservations(observations, mainState, mainThread, throwInWork);
		Assert.Equal(0, LuaApi.lua_gettop((lua_State*) mainState));
		Assert.Equal(0, LuaApi.lua_gettop(root.L));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait("Category", "NativeLua")]
	public void Dispatch_releases_its_callback_on_the_original_worker_state_after_success_or_managed_failure(
		bool throwInWork)
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState root = new();
		using ThreadStates states = new(root);
		using HostSimulator host = new();
		HostingTest.Enable(host, root);
		nint mainState = states.GetState();
		int mainThread = Environment.CurrentManagedThreadId;
		using SynchronizeStandIn synchronize = new(new LuaState(mainState));
		OneShotStateProvider provider = new(states);
		FakeExports.StateProviderForTests = provider.GetState;
		int exportCallsBefore = FakeExports.GetLuaStateCalls;
		StrongBox<OriginalStateReleaseObservation?> observation = new();
		const long sentinel = 0x53388;

		Task worker = Task.Factory.StartNew(() =>
		{
			nint workerState = states.GetState();
			LuaState workerLua = new(workerState);
			workerLua.PushInteger(sentinel);
			int before = workerLua.Top;
			StrongBox<int> callbackThread = new();
			StrongBox<int> executions = new();
			Exception? failure = Record.Exception(() => MainThread.Invoke(_ =>
			{
				callbackThread.Value = Environment.CurrentManagedThreadId;
				executions.Value++;
				if (throwInWork)
				{
					throw new NotSupportedException("managed dispatch failure");
				}
			}, 0));
			int after = workerLua.Top;
			bool readSentinel = workerLua.TryReadInteger(1, out long observedSentinel);
			workerLua.SetTop(0);
			observation.Value = new OriginalStateReleaseObservation(workerState, before, after, readSentinel,
				observedSentinel, callbackThread.Value, executions.Value, failure);
		}, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

		try
		{
			Assert.True(synchronize.PumpOne(TimeSpan.FromSeconds(5)), "Worker did not queue the expected dispatch.");
		}
		finally
		{
			synchronize.StopAndDrain();
			Join(worker);
		}

		OriginalStateReleaseObservation result = Assert.IsType<OriginalStateReleaseObservation>(observation.Value);
		VerifyOriginalStateRelease(result, provider, exportCallsBefore, sentinel, mainState, root);
		Assert.Equal(mainThread, result.CallbackThread);
		Assert.Equal(1, result.Executions);
		VerifyManagedDispatchFailure(result.Failure, throwInWork);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Synchronize_Lua_error_survives_same_state_callback_release_and_restores_the_worker_stack()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState root = new();
		using ThreadStates states = new(root);
		using HostSimulator host = new();
		HostingTest.Enable(host, root);
		nint mainState = states.GetState();
		LuaState setupState = new(mainState);
		using (LuaFrame frame = new(setupState))
		{
			Assert.True(setupState.TryExecute(
				"function synchronize(f, ...) error('same-state synchronize failure', 0) end"u8, 0).IsOk);
		}

		OneShotStateProvider provider = new(states);
		FakeExports.StateProviderForTests = provider.GetState;
		int exportCallsBefore = FakeExports.GetLuaStateCalls;
		StrongBox<OriginalStateReleaseObservation?> observation = new();
		StrongBox<int> executions = new();
		const long sentinel = 0xC511;

		Task worker = Task.Factory.StartNew(() =>
		{
			nint workerState = states.GetState();
			LuaState workerLua = new(workerState);
			workerLua.PushInteger(sentinel);
			int before = workerLua.Top;
			Exception? failure = Record.Exception(() => MainThread.Invoke(static box => box.Value++, executions));
			int after = workerLua.Top;
			bool readSentinel = workerLua.TryReadInteger(1, out long observedSentinel);
			workerLua.SetTop(0);
			observation.Value = new OriginalStateReleaseObservation(workerState, before, after, readSentinel,
				observedSentinel, 0, executions.Value, failure);
		}, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

		Join(worker);

		OriginalStateReleaseObservation result = Assert.IsType<OriginalStateReleaseObservation>(observation.Value);
		InvalidOperationException exception = Assert.IsType<InvalidOperationException>(result.Failure);
		Assert.Equal("The host's 'synchronize' call failed: same-state synchronize failure", exception.Message);
		VerifyOriginalStateRelease(result, provider, exportCallsBefore, sentinel, mainState, root);
		Assert.Equal(0, result.Executions);
	}

	private static void VerifyOriginalStateRelease(OriginalStateReleaseObservation result, OneShotStateProvider provider,
		int exportCallsBefore, long sentinel, nint mainState, NativeLuaState root)
	{
		Assert.Equal(1, provider.Calls);
		Assert.Equal(1, FakeExports.GetLuaStateCalls - exportCallsBefore);
		Assert.Equal(0, LuaCallbackRegistry.Count);
		Assert.Equal(1, result.Before);
		Assert.Equal(result.Before, result.After);
		Assert.True(result.ReadSentinel);
		Assert.Equal(sentinel, result.Sentinel);
		Assert.Equal(0, LuaApi.lua_gettop((lua_State*) result.WorkerState));
		Assert.Equal(0, LuaApi.lua_gettop((lua_State*) mainState));
		Assert.Equal(0, LuaApi.lua_gettop(root.L));
	}

	private static void VerifyManagedDispatchFailure(Exception? failure, bool expected)
	{
		if (expected)
		{
			Assert.Equal("managed dispatch failure", Assert.IsType<NotSupportedException>(failure).Message);
		}
		else
		{
			Assert.Null(failure);
		}
	}

	private static void VerifyObservations(IEnumerable<Observation> observations, nint mainState, int mainThread,
		bool throwInWork)
	{
		Assert.All(observations, observation =>
		{
			Assert.Equal(0, observation.Before);
			Assert.Equal(0, observation.After);
			Assert.NotEqual(mainState, observation.WorkerState);
			Assert.Equal(mainState, observation.CallbackState);
			Assert.Equal(mainThread, observation.CallbackThread);
			if (throwInWork)
			{
				Assert.Equal("owned callback failure", Assert.IsType<NotSupportedException>(observation.Failure).Message);
			}
			else
			{
				Assert.Null(observation.Failure);
			}
		});
	}

	private static void Join(Task worker)
	{
		// Never release Lua while a worker still owns it. The isolated test process has an external deadline;
		// a broken dispatcher that cannot drain is terminated there, without disposing a live state here.
		worker.GetAwaiter().GetResult();
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Queued_stop_drains_the_worker_without_running_work_or_releasing_a_live_coroutine()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState root = new();
		using ThreadStates states = new(root);
		using HostSimulator host = new();
		HostingTest.Enable(host, root);
		FakeExports.StateProviderForTests = states.GetState;
		nint mainState = states.GetState();
		using SynchronizeStandIn synchronize = new(new LuaState(mainState));
		StrongBox<nint> workerState = new();
		StrongBox<Exception?> failure = new();
		int[] executions = [0];
		Task worker = Task.Factory.StartNew(() =>
		{
			workerState.Value = states.GetState();
			failure.Value = Record.Exception(() => MainThread.Invoke(static box => box[0]++, executions));
		}, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		try
		{
			Assert.True(synchronize.WaitForPending(TimeSpan.FromSeconds(5)));
		}
		finally
		{
			synchronize.StopAndDrain();
			Join(worker);
		}

		Assert.Contains("returned without running the dispatched work",
			Assert.IsType<InvalidOperationException>(failure.Value).Message, StringComparison.Ordinal);
		Assert.Equal(0, executions[0]);
		Assert.Equal(0, synchronize.PumpedCalls);
		Assert.NotEqual(0, workerState.Value);
		Assert.Equal(0, LuaApi.lua_gettop((lua_State*) workerState.Value));
		Assert.Equal(0, LuaApi.lua_gettop((lua_State*) mainState));
		Assert.Equal(0, LuaApi.lua_gettop(root.L));
	}

	private sealed record Observation(int Before, int After, nint WorkerState, nint CallbackState,
		int CallbackThread, Exception? Failure);

	private sealed record OriginalStateReleaseObservation(nint WorkerState, int Before, int After,
		bool ReadSentinel, long Sentinel, int CallbackThread, int Executions, Exception? Failure);

	private sealed class OneShotStateProvider(ThreadStates states)
	{
		private int _calls;

		public int Calls => Volatile.Read(ref _calls);

		public nint GetState()
		{
			// Returning null after the first lookup makes a cleanup-time provider reacquisition deterministic without
			// violating the host contract for the state admitted at the start of the operation.
			return Interlocked.Increment(ref _calls) == 1 ? states.GetState() : 0;
		}
	}

	private sealed class ThreadStates(NativeLuaState root) : IDisposable
	{
		private readonly Lock _gate = new();
		private readonly Dictionary<int, (nint State, int Reference)> _states = [];

		public nint GetState()
		{
			lock (_gate)
			{
				int thread = Environment.CurrentManagedThreadId;
				if (_states.TryGetValue(thread, out (nint State, int Reference) existing))
				{
					return existing.State;
				}

				lua_State* coroutine = LuaApi.lua_newthread(root.L);
				int reference = LuaApi.luaL_ref(root.L, LuaApi.LUA_REGISTRYINDEX);
				_states.Add(thread, ((nint) coroutine, reference));
				return (nint) coroutine;
			}
		}

		public void Dispose()
		{
			foreach ((nint _, int reference) in _states.Values)
			{
				LuaApi.luaL_unref(root.L, LuaApi.LUA_REGISTRYINDEX, reference);
			}
		}
	}

	// CE's public 7.5 host implementation saves the function from the worker state; TLuaCaller.synchronize
	// invokes it on that same state with one nil argument and one result. The installed 7.7 contract is
	// modeled here; its private implementation remains to be observed on the real host.
	private sealed class SynchronizeStandIn : IDisposable
	{
		private static SynchronizeStandIn? _active;
		private readonly AutoResetEvent _queued = new(initialState: false);
		private readonly ConcurrentQueue<PendingCall> _pending = new();
		private readonly Lock _gate = new();
		private bool _stopping;

		public SynchronizeStandIn(LuaState main)
		{
			_active = this;
			Assert.True(main.TryPushFunction(new LuaNativeFunction(&Synchronize)).IsOk);
			Assert.True(main.TrySetGlobal("synchronize"u8).IsOk);
		}

		public int PumpedCalls
		{
			get; private set;
		}

		public bool IsStopping => Volatile.Read(ref _stopping);

		public bool WaitForPending(TimeSpan timeout)
		{
			return _queued.WaitOne(timeout);
		}

		public bool PumpOne(TimeSpan timeout)
		{
			if (!_queued.WaitOne(timeout) || !_pending.TryDequeue(out PendingCall? call))
			{
				return false;
			}

			int status = -1;
			try
			{
				lua_State* state = (lua_State*) call.State;
				LuaApi.lua_rawgeti(state, LuaApi.LUA_REGISTRYINDEX, call.Reference);
				LuaApi.lua_pushnil(state);
				status = LuaApi.lua_pcall(state, 1, 1, 0);
				PumpedCalls++;
			}
			finally
			{
				call.Completed.SetResult(status);
			}

			Assert.Equal(0, status);
			return true;
		}

		public void StopAndDrain()
		{
			lock (_gate)
			{
				Volatile.Write(ref _stopping, true);
				while (_pending.TryDequeue(out PendingCall? call))
				{
					// Cancel without touching its Lua state. Only the awakened worker releases the reference.
					call.Completed.SetResult(-1);
				}
			}
		}

		public void Dispose()
		{
			StopAndDrain();
			_active = null;
			_queued.Dispose();
		}

		[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
		private static int Synchronize(nint pointer)
		{
			try
			{
				lua_State* state = (lua_State*) pointer;
				LuaApi.lua_pushvalue(state, 1);
				PendingCall call = new(pointer, LuaApi.luaL_ref(state, LuaApi.LUA_REGISTRYINDEX));
				SynchronizeStandIn active = _active!;
				lock (active._gate)
				{
					if (active.IsStopping)
					{
						LuaApi.luaL_unref(state, LuaApi.LUA_REGISTRYINDEX, call.Reference);
						return 0;
					}
					active._pending.Enqueue(call);
					active._queued.Set();
				}
				int status = call.Completed.Task.GetAwaiter().GetResult();
				LuaApi.luaL_unref(state, LuaApi.LUA_REGISTRYINDEX, call.Reference);
				return status == 0 ? 1 : 0;
			}
			catch
			{
				// Exceptions must never escape an unmanaged callback.
				return 0;
			}
		}

		private sealed class PendingCall(nint state, int reference)
		{
			public nint State { get; } = state;
			public int Reference { get; } = reference;
			public TaskCompletionSource<int> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}
}
