using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>
///     Qualification of the per-thread state contract: worker coroutine pointers are distinct Lua stacks in one
///     attachment/generation universe, not separately owned heaps.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class LuaUniverseQualificationTests
{
	private static unsafe LuaNativeFunction ReturnOneFunction => new(&ReturnOne);

	[Fact]
	public void First_worker_acquisition_rejects_a_missing_state_then_uses_a_distinct_coroutine_in_the_same_universe()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		main.PushInteger(719);
		Assert.True(main.TrySetGlobal("sdk012_shared_universe"u8).IsOk);
		main.PushString("shared private registry"u8);
		LuaRef sharedReference = main.CreateRef();
		using RootedCoroutine worker = RootedCoroutine.Create(main);
		LuaStateIdentity identity = LuaRuntime.CurrentStateIdentity;
		FirstWorkerObservation observation = ObserveFirstWorker(worker, sharedReference);

		Assert.Null(observation.Failure);
		Assert.True(observation.FirstAcquisitionWasRejected);
		Assert.Equal(2, HostDouble.ProviderCalls);
		Assert.NotEqual(main.Handle, observation.WorkerState);
		Assert.Equal(identity, observation.WorkerIdentity);
		Assert.Equal(719L, observation.GlobalValue);
		Assert.Equal("shared private registry", observation.ReferenceValue);
		Assert.Equal(0, main.Top);

		sharedReference.Release(main);
	}

	[Fact]
	public void
		State_reset_generation_invalidates_shared_registry_references_and_callbacks_before_a_fresh_worker_state()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using RootedCoroutine worker = RootedCoroutine.Create(main);
		using NativeLuaState replacementState = new();
		LuaState replacementMain = LuaTest.View(replacementState);
		using RootedCoroutine replacementWorker = RootedCoroutine.Create(replacementMain);
		main.PushString("before reset"u8);
		LuaRef reference = main.CreateRef();
		Assert.True(
			LuaCallback.TryCreate(main, ReturnOneFunction, new object(), out LuaCallback<object>? callback).IsOk);
		Assert.NotNull(callback);
		Assert.True(callback.TryRegister(main, "sdk012_reset_callback"u8).IsOk);
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;

		using (LuaRuntime.BeginStateReset())
		{
			HostDouble.SetStateForCurrentThread(replacementWorker.State.Handle);
		}

		LuaStateIdentity after = LuaRuntime.CurrentStateIdentity;
		Assert.Equal(before.AttachEpoch, after.AttachEpoch);
		Assert.Equal(before.StateGeneration + 1, after.StateGeneration);
		Assert.True(reference.IsResolved);
		Assert.False(reference.IsCurrent);
		Assert.True(callback.IsReleased);
		Assert.False(callback.IsCurrent);
		PostResetWorkerObservation observation = ObservePostResetWorker(replacementWorker, reference, callback);

		Assert.Null(observation.Failure);
		Assert.NotEqual(worker.State.Handle, observation.WorkerState);
		Assert.Equal(replacementWorker.State.Handle, observation.WorkerState);
		Assert.False(observation.ReferencePushed);
		Assert.False(observation.CallbackPushed);
		Assert.True(observation.CallbackStatus.IsOk);
		Assert.Contains("nil value", observation.CallbackError, StringComparison.Ordinal);

		reference.Release(default);
	}

	private static FirstWorkerObservation ObserveFirstWorker(RootedCoroutine worker, LuaRef sharedReference)
	{
		FirstWorkerObservation observation = new();
		Thread thread = new(() => ObserveFirstWorkerCore(worker, sharedReference, observation));
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The first worker-state acquisition did not return.");
		return observation;
	}

	private static void ObserveFirstWorkerCore(
		RootedCoroutine worker,
		LuaRef sharedReference,
		FirstWorkerObservation observation)
	{
		try
		{
			HostDouble.ClearStateForCurrentThread();
			observation.FirstAcquisitionWasRejected =
				!LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation unavailable);
			unavailable.Dispose();
			HostDouble.SetStateForCurrentThread(worker.State.Handle);
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			LuaState workerLua = operation.State;
			observation.WorkerState = workerLua.Handle;
			observation.WorkerIdentity = LuaRuntime.CurrentStateIdentity;
			Assert.True(workerLua.TryGetGlobal("sdk012_shared_universe"u8).IsOk);
			Assert.True(workerLua.TryReadInteger(-1, out long globalValue));
			observation.GlobalValue = globalValue;
			workerLua.Pop(1);
			Assert.True(workerLua.TryPushRef(sharedReference));
			Assert.True(workerLua.TryReadString(-1, out string? referenceValue));
			observation.ReferenceValue = referenceValue;
			workerLua.Pop(1);
			Assert.Equal(0, workerLua.Top);
		}
		catch (Exception exception)
		{
			observation.Failure = exception;
		}
	}

	private static PostResetWorkerObservation ObservePostResetWorker(
		RootedCoroutine worker,
		LuaRef reference,
		LuaCallback callback)
	{
		PostResetWorkerObservation observation = new();
		Thread thread = new(() => ObservePostResetWorkerCore(worker, reference, callback, observation));
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The post-reset worker did not return.");
		return observation;
	}

	private static void ObservePostResetWorkerCore(
		RootedCoroutine worker,
		LuaRef reference,
		LuaCallback callback,
		PostResetWorkerObservation observation)
	{
		try
		{
			HostDouble.SetStateForCurrentThread(worker.State.Handle);
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			LuaState workerLua = operation.State;
			observation.WorkerState = workerLua.Handle;
			observation.ReferencePushed = workerLua.TryPushRef(reference);
			observation.CallbackPushed = callback.TryPush(workerLua);
			observation.CallbackStatus = workerLua.TryExecute(
				"local ok, err = pcall(sdk012_reset_callback) return ok, err"u8, 2);
			if (observation.CallbackStatus.IsOk)
			{
				Assert.False(workerLua.ToBoolean(-2));
				observation.CallbackError = LuaTest.ReadString(workerLua, -1);
				workerLua.Pop(2);
			}

			Assert.Equal(0, workerLua.Top);
		}
		catch (Exception exception)
		{
			observation.Failure = exception;
		}
	}

	[UnmanagedCallersOnly(
		CallConvs = [typeof(CallConvCdecl)])]
	private static int ReturnOne(nint pointer)
	{
		LuaState state = new(pointer);
		try
		{
			state.PushInteger(1);
			return 1;
		}
		catch (Exception exception)
		{
			return LuaThunk.Fail(state, exception);
		}
	}

	private sealed class FirstWorkerObservation
	{
		public Exception? Failure
		{
			get;
			set;
		}

		public bool FirstAcquisitionWasRejected
		{
			get;
			set;
		}

		public long GlobalValue
		{
			get;
			set;
		}

		public string? ReferenceValue
		{
			get;
			set;
		}

		public LuaStateIdentity WorkerIdentity
		{
			get;
			set;
		}

		public nint WorkerState
		{
			get;
			set;
		}
	}

	private sealed class PostResetWorkerObservation
	{
		public bool CallbackPushed
		{
			get;
			set;
		} = true;

		public string CallbackError
		{
			get;
			set;
		} = string.Empty;

		public LuaStatus CallbackStatus
		{
			get;
			set;
		}

		public Exception? Failure
		{
			get;
			set;
		}

		public bool ReferencePushed
		{
			get;
			set;
		} = true;

		public nint WorkerState
		{
			get;
			set;
		}
	}

	private sealed unsafe class RootedCoroutine : IDisposable
	{
		private readonly LuaRef _root;

		private RootedCoroutine(LuaState state, LuaRef root)
		{
			State = state;
			_root = root;
		}

		public LuaState State
		{
			get;
		}

		public void Dispose()
		{
			if (!LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation))
			{
				_root.Release(default);
				return;
			}

			using (operation)
			{
				_root.Release(operation.State);
			}
		}

		public static RootedCoroutine Create(LuaState main)
		{
			LuaStatus status = main.TryExecute("return coroutine.create(function() end)"u8, 1);
			Assert.True(status.IsOk);
			lua_State* pointer = lua_tothread(main.Pointer, -1);
			Assert.NotEqual(nint.Zero, (nint) pointer);
			LuaState coroutine = new(pointer);
			coroutine.Pop(1);
			return new RootedCoroutine(coroutine, main.CreateRef());
		}
	}
}
