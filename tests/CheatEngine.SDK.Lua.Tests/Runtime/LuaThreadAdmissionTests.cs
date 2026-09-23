using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>
///     The 2.0 conservative-by-default worker-thread admission policy (F04, ADR-07): a new
///     <see cref="LuaRuntime.AcquireOperation()" />-family call is refused on a worker thread before the host's state
///     provider ever runs, unless the calling thread is the captured main thread, the call is nested inside
///     already-admitted work, or the worker-side <c>synchronize</c> hand-off admits it explicitly.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class LuaThreadAdmissionTests
{
	private static LuaAdmissionStatus? s_nestedStatus;

	[Fact]
	[Trait("Qualification", "Q19")]
	public void Default_admission_refuses_a_worker_before_calling_the_state_provider()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);
		int providerCallsBefore = HostDouble.ProviderCalls;

		LuaAdmissionStatus status = RunOnWorker(() =>
		{
			HostDouble.SetStateForCurrentThread(state.Pointer);
			LuaAdmissionStatus result = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
			operation.Dispose();
			return result;
		});

		Assert.Equal(LuaAdmissionStatus.ThreadNotAdmitted, status);
		Assert.Equal(providerCallsBefore, HostDouble.ProviderCalls);
		Assert.Equal(LuaThreadAdmission.MainThreadOnly, LuaRuntime.ThreadAdmission);
	}

	[Fact]
	[Trait("Qualification", "Q19")]
	public void Two_workers_acquiring_at_the_same_time_are_both_refused_by_default()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);
		int providerCallsBefore = HostDouble.ProviderCalls;
		using Barrier barrier = new(2);
		LuaAdmissionStatus[] results = new LuaAdmissionStatus[2];

		void Worker(int index)
		{
			HostDouble.SetStateForCurrentThread(state.Pointer);
			barrier.SignalAndWait(TimeSpan.FromSeconds(5));
			results[index] = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
			operation.Dispose();
		}

		Thread first = new(() => Worker(0));
		Thread second = new(() => Worker(1));
		first.Start();
		second.Start();
		Assert.True(first.Join(TimeSpan.FromSeconds(5)));
		Assert.True(second.Join(TimeSpan.FromSeconds(5)));

		Assert.Equal(LuaAdmissionStatus.ThreadNotAdmitted, results[0]);
		Assert.Equal(LuaAdmissionStatus.ThreadNotAdmitted, results[1]);
		Assert.Equal(providerCallsBefore, HostDouble.ProviderCalls);
	}

	[Fact]
	public void The_captured_main_thread_is_admitted_by_default()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);

		LuaAdmissionStatus status = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
		operation.Dispose();

		Assert.Equal(LuaAdmissionStatus.Admitted, status);
	}

	[Fact]
	[Trait("Qualification", "Q19")]
	public void A_host_invoked_callback_on_a_worker_admits_nested_operations_on_that_thread()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		s_nestedStatus = null;
		Assert.True(LuaCallback.TryCreate(main, new LuaNativeFunction(&NestedAcquireThunk), new object(),
			out LuaCallback<object>? callback).IsOk);
		Assert.True(callback!.TryRegister(main, "nested_acquire"u8).IsOk);
		LuaState workerCoroutine = CreateRootedCoroutine(main);

		LuaStatus workerCallStatus = RunOnWorker(() =>
		{
			HostDouble.SetStateForCurrentThread(workerCoroutine.Handle);
			return workerCoroutine.TryExecute("nested_acquire()"u8, 0);
		});

		Assert.True(workerCallStatus.IsOk);
		Assert.Equal(LuaAdmissionStatus.Admitted, s_nestedStatus);
		callback.Release(main);
	}

	[Fact]
	public void Worker_admission_opt_in_admits_workers_until_the_next_attach()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state, admitWorkerThreads: true);

		LuaAdmissionStatus status = RunOnWorker(() =>
		{
			HostDouble.SetStateForCurrentThread(state.Pointer);
			LuaAdmissionStatus result = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
			operation.Dispose();
			return result;
		});

		Assert.Equal(LuaAdmissionStatus.Admitted, status);
	}

	[Fact]
	public void Worker_admission_opt_in_is_refused_while_detached()
	{
		LuaTest.RequireNativeLua();
		LuaRuntime.Detach();

#pragma warning disable CESDK5001 // Exercises the gated opt-in itself, while detached, to prove it throws.
		Assert.Throws<InvalidOperationException>(LuaRuntime.AdmitWorkerThreads);
#pragma warning restore CESDK5001
	}

	[Fact]
	public void Detach_and_attach_restore_main_thread_only_admission()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using (RuntimeScope firstAttach = new(state, admitWorkerThreads: true))
		{
			Assert.Equal(LuaThreadAdmission.WorkerThreads, LuaRuntime.ThreadAdmission);
		}

		Assert.Equal(LuaThreadAdmission.MainThreadOnly, LuaRuntime.ThreadAdmission);

		using RuntimeScope secondAttach = new(state);
		Assert.Equal(LuaThreadAdmission.MainThreadOnly, LuaRuntime.ThreadAdmission);
	}

	[Fact]
	[Trait("Qualification", "Q19")]
	public void Callback_dispose_on_a_non_admitted_worker_defers_neutralization_to_detach()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Assert.True(LuaCallback.TryCreate(main, new LuaNativeFunction(&NestedAcquireThunk), new object(),
			out LuaCallback<object>? callback).IsOk);
		Assert.NotNull(callback);

		RunOnWorker(() =>
		{
			HostDouble.SetStateForCurrentThread(main.Handle);
			callback.Dispose();
			return 0;
		});

		// A worker thread cannot obtain admission by default, so Dispose defers to the transition instead of
		// abandoning the closure early: the callback remains linked until Detach neutralizes it.
		Assert.False(callback.IsReleased);
		Assert.Equal(1, LuaCallbackRegistry.Count);

		LuaRuntime.Detach();
		Assert.True(callback.IsReleased);
	}

	[Fact]
	public void The_worker_refusal_is_reported_once_per_attachment_through_the_diagnostic_observer()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);
		int raisedCount = 0;
		LuaRuntimeDiagnostic? lastKind = null;
		LuaRuntime.DiagnosticObserver = kind =>
		{
			raisedCount++;
			lastKind = kind;
		};

		try
		{
			for (int i = 0; i < 3; i++)
			{
				RunOnWorker(() =>
				{
					HostDouble.SetStateForCurrentThread(state.Pointer);
					LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
					operation.Dispose();
					return 0;
				});
			}

			Assert.Equal(1, raisedCount);
			Assert.Equal(LuaRuntimeDiagnostic.WorkerThreadRefused, lastKind);
		}
		finally
		{
			LuaRuntime.DiagnosticObserver = null;
		}
	}

	[Fact]
	public void Main_thread_dispatch_admission_is_the_only_worker_path_admitted_by_default()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);

		LuaAdmissionStatus workerDirect = RunOnWorker(() =>
		{
			HostDouble.SetStateForCurrentThread(state.Pointer);
			LuaAdmissionStatus result = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation op);
			op.Dispose();
			return result;
		});

		bool dispatchAdmitted = RunOnWorker(() =>
		{
			HostDouble.SetStateForCurrentThread(state.Pointer);
			try
			{
				using LuaRuntimeOperation operation = LuaRuntime.AcquireOperationForMainThreadDispatch();
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		});

		Assert.Equal(LuaAdmissionStatus.ThreadNotAdmitted, workerDirect);
		Assert.True(dispatchAdmitted);
	}

	[Fact]
	public void LuaRuntimeOperation_is_a_ref_struct_so_it_cannot_cross_an_await()
	{
		Assert.True(typeof(LuaRuntimeOperation).IsByRefLike);
	}

	[Fact]
	public void Worker_admission_gate_is_experimental_CESDK5001_with_the_documentation_url()
	{
		MethodInfo method = typeof(LuaRuntime).GetMethod(nameof(LuaRuntime.AdmitWorkerThreads))!;
		ExperimentalAttribute? attribute = method.GetCustomAttribute<ExperimentalAttribute>();

		Assert.NotNull(attribute);
		Assert.Equal("CESDK5001", attribute!.DiagnosticId);
		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md",
			attribute.UrlFormat);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int NestedAcquireThunk(nint pointer)
	{
		LuaState state = new(pointer);
		try
		{
			s_nestedStatus = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
			operation.Dispose();
			return 0;
		}
		catch (Exception exception)
		{
			return LuaThunk.Fail(state, exception);
		}
	}

	private static LuaState CreateRootedCoroutine(LuaState main)
	{
		LuaStatus status = main.TryExecute("return coroutine.create(function() end)"u8, 1);
		Assert.True(status.IsOk);
		lua_State* pointer = lua_tothread(main.Pointer, -1);
		Assert.NotEqual(nint.Zero, (nint) pointer);
		main.Pop(1);
		return new LuaState(pointer);
	}

	private static T RunOnWorker<T>(Func<T> body)
	{
		T result = default!;
		Exception? failure = null;
		Thread thread = new(() =>
		{
			try
			{
				result = body();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The worker thread did not return.");
		if (failure is not null)
		{
			throw failure;
		}

		return result;
	}
}
