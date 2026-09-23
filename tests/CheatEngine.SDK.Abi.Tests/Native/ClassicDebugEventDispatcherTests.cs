using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Contract tests for the SDK-owned classic type-2 callback dispatcher. The simulated host calls exactly the same
///     stdcall function pointer that a classic host stores from <see cref="DebugEventPluginInit" />; it is not live
///     Cheat Engine qualification.
/// </summary>
/// <remarks>
///     <para>
///         Every dispatcher test lives in this one class on purpose: the dispatcher's single active registration and the
///         fake host's seams are process statics, and a second test class would run in parallel and collide.
///     </para>
///     <para>
///         The Q38 tests call the registered thunk from threads created with Win32 <c>CreateThread</c>, which the CLR did
///         not create and has never seen before the call (the situation of a debug event delivered by a host thread). They
///         are C1 evidence of the dispatcher's thread contract only: on the managed-hostfxr profile no route reaches the
///         classic <c>RegisterFunction</c>, so Q38 at C3 stays NotApplicable (see <c>libs/CheatEngine.SDK.Abi/README.md</c>).
///     </para>
/// </remarks>
public sealed unsafe partial class ClassicDebugEventDispatcherTests : IDisposable
{
	private const uint WaitObject0 = 0;
	private const uint NativeThreadTimeoutMilliseconds = 30_000;
	private const int DefaultFunctionId = 901;
	private static readonly TimeSpan ReleaseTaskTimeout = TimeSpan.FromSeconds(30);

	private static delegate* unmanaged[Stdcall]<void*, int> s_callback;
	private static int s_unregisterCalls;
	private static bool s_unregisterSucceeds;
	private static ManualResetEventSlim? s_unregisterEntered;
	private static int s_registerCalls;
	private static int s_functionIdToReturn = DefaultFunctionId;
	private static int s_lastUnregisteredFunctionId = -1;
	private ClassicDebugEventDispatcher? _dispatcher;

	/// <inheritdoc />
	public void Dispose()
	{
		s_unregisterSucceeds = true;
		s_unregisterEntered = null;
		_dispatcher?.TryRelease();
		_dispatcher = null;
		s_callback = null;
		s_unregisterCalls = 0;
		s_registerCalls = 0;
		s_functionIdToReturn = DefaultFunctionId;
		s_lastUnregisteredFunctionId = -1;
	}

	[Fact]
	public void Callback_runs_the_synchronous_handler_with_a_scalar_copy_and_leaves_continuation_to_CheatEngine()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		DebugEventObservation received = default;
		int handlerCalls = 0;
		ClassicDebugEventDispatcher dispatcher = Register((in observation) =>
		{
			handlerCalls++;
			received = observation;
			return DebugEventDecision.ContinueWithCheatEngine;
		});
		NativeDebugEvent nativeEvent = new(0x0000_0006, 101, 202);

		int result = Invoke(&nativeEvent);

		Assert.Equal(0, result);
		Assert.Equal(1, handlerCalls);
		Assert.Equal(0x0000_0006u, received.EventCode);
		Assert.Equal(101u, received.ProcessId);
		Assert.Equal(202u, received.ThreadId);
		Assert.True(received.SequenceNumber > 0);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	public void Callback_copies_observations_before_the_native_buffer_can_be_reused()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		BoundedDebugEventObservationBuffer observations = new(1, DebugEventObservationOverflowPolicy.DropNewest);
		ClassicDebugEventDispatcher dispatcher = Register(static (in _) => DebugEventDecision.ContinueWithCheatEngine,
			observations);
		NativeDebugEvent nativeEvent = new(3, 404, 505);

		Assert.Equal(0, Invoke(&nativeEvent));
		nativeEvent = new NativeDebugEvent(99, 0, 0);

		Assert.True(observations.TryRead(out DebugEventObservation copied));
		Assert.Equal(3u, copied.EventCode);
		Assert.Equal(404u, copied.ProcessId);
		Assert.Equal(505u, copied.ThreadId);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	public void Plugin_owned_continuation_request_is_rejected_to_the_current_CheatEngine_owned_fallback()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		ClassicDebugEventDispatcher dispatcher = Register(static (in _) => DebugEventDecision.PluginOwnsContinuation);
		NativeDebugEvent nativeEvent = new(1, 2, 3);

		Assert.Equal(0, Invoke(&nativeEvent));
		Assert.Equal(1, dispatcher.UnsupportedContinuationRequestCount);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	public void Callback_exception_is_contained_and_returns_the_CheatEngine_owned_fallback()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		ClassicDebugEventDispatcher dispatcher = Register(static (in _) => throw new InvalidOperationException("boom"));
		NativeDebugEvent nativeEvent = new(1, 2, 3);

		int result = 1;
		Exception? exception = Record.Exception(() =>
		{
			result = InvokeValue(nativeEvent);
		});

		Assert.Null(exception);
		Assert.Equal(0, result);
		Assert.Equal(1, dispatcher.CallbackFailureCount);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	public void Cancelled_registration_and_null_event_do_not_invoke_user_code()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		using CancellationTokenSource cancellation = new();
		int handlerCalls = 0;
		ClassicDebugEventDispatcher dispatcher = RegisterWithCancellation((in _) =>
		{
			handlerCalls++;
			return DebugEventDecision.ContinueWithCheatEngine;
		}, null, cancellation.Token);
		cancellation.Cancel();

		Assert.Equal(0, Invoke(null));
		NativeDebugEvent nativeEvent = new(1, 2, 3);
		Assert.Equal(0, Invoke(&nativeEvent));
		Assert.Equal(0, handlerCalls);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	public void Full_observation_buffer_drops_only_observation_and_never_changes_the_native_result()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		BoundedDebugEventObservationBuffer observations = new(1, DebugEventObservationOverflowPolicy.DropNewest);
		ClassicDebugEventDispatcher dispatcher = Register(static (in _) => DebugEventDecision.ContinueWithCheatEngine,
			observations);
		NativeDebugEvent first = new(1, 2, 3);
		NativeDebugEvent second = new(2, 3, 4);

		Assert.Equal(0, Invoke(&first));
		Assert.Equal(0, Invoke(&second));
		Assert.Equal(1, observations.DroppedObservationCount);
		Assert.True(observations.TryRead(out DebugEventObservation retained));
		Assert.Equal(1u, retained.EventCode);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	public void Release_closes_admission_before_unregistration_failure_and_keeps_the_root_for_retry()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		int handlerCalls = 0;
		ClassicDebugEventDispatcher dispatcher = Register((in _) =>
		{
			handlerCalls++;
			return DebugEventDecision.ContinueWithCheatEngine;
		});
		s_unregisterSucceeds = false;

		Assert.Equal(ClassicDebugEventReleaseStatus.UnregisterUnconfirmed, dispatcher.TryRelease());
		NativeDebugEvent lateEvent = new(1, 2, 3);
		Assert.Equal(0, Invoke(&lateEvent));
		Assert.Equal(0, handlerCalls);

		s_unregisterSucceeds = true;
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
		Assert.Equal(2, s_unregisterCalls);
	}

	[Fact]
	public void Release_waits_for_an_admitted_callback_and_rejects_a_late_callback()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		using ManualResetEventSlim handlerEntered = new();
		using ManualResetEventSlim allowHandlerToReturn = new();
		using ManualResetEventSlim unregisterEntered = new();
		using ManualResetEventSlim callbackCompleted = new();
		using ManualResetEventSlim releaseCompleted = new();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		s_unregisterEntered = unregisterEntered;
		int handlerCalls = 0;
		ClassicDebugEventDispatcher dispatcher = Register((in _) =>
		{
			handlerCalls++;
			handlerEntered.Set();
			allowHandlerToReturn.Wait(cancellationToken);
			return DebugEventDecision.ContinueWithCheatEngine;
		});
		NativeDebugEvent* eventMemory = (NativeDebugEvent*) NativeMemory.Alloc((nuint) sizeof(NativeDebugEvent));
		*eventMemory = new NativeDebugEvent(1, 2, 3);
		int callbackResult = 1;
		ClassicDebugEventReleaseStatus releaseResult = default;

		try
		{
			IntPtr eventAddress = (nint) eventMemory;
			_ = Task.Run(() =>
			{
				callbackResult = Invoke((NativeDebugEvent*) eventAddress);
				callbackCompleted.Set();
			}, cancellationToken);
			handlerEntered.Wait(cancellationToken);
			_ = Task.Run(() =>
			{
				releaseResult = dispatcher.TryRelease();
				releaseCompleted.Set();
			}, cancellationToken);
			unregisterEntered.Wait(cancellationToken);

			NativeDebugEvent lateEvent = new(4, 5, 6);
			Assert.Equal(0, Invoke(&lateEvent));
			Assert.Equal(1, handlerCalls);
			Assert.False(releaseCompleted.IsSet);
			Assert.Equal(ClassicDebugEventReleaseStatus.ReleaseInProgress, dispatcher.TryRelease());

			allowHandlerToReturn.Set();
			callbackCompleted.Wait(cancellationToken);
			releaseCompleted.Wait(cancellationToken);
			Assert.Equal(0, callbackResult);
			Assert.Equal(ClassicDebugEventReleaseStatus.Released, releaseResult);
		}
		finally
		{
			// The release task starts right after the handler signals that it entered.
			FinishBlockedWork(allowHandlerToReturn, callbackCompleted, handlerEntered.IsSet ? releaseCompleted : null);
			NativeMemory.Free(eventMemory);
		}
	}

	[Fact]
	public void Release_reentered_from_a_handler_is_refused_without_deadlocking_or_freeing_the_target()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		ClassicDebugEventDispatcher? dispatcher = null;
		ClassicDebugEventReleaseStatus status = default;
		dispatcher = Register((in _) =>
		{
			status = dispatcher!.TryRelease();
			return DebugEventDecision.ContinueWithCheatEngine;
		});
		NativeDebugEvent nativeEvent = new(1, 2, 3);

		Assert.Equal(0, Invoke(&nativeEvent));
		Assert.Equal(ClassicDebugEventReleaseStatus.CallbackIsExecuting, status);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	[Trait("Qualification", "Q38")]
	public void Callback_from_a_native_os_thread_is_admitted_copied_and_returns_the_CheatEngine_fallback()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		uint handlerNativeThreadId = 0;
		int handlerManagedThreadId = 0;
		int handlerCalls = 0;
		DebugEventObservation received = default;
		BoundedDebugEventObservationBuffer observations = new(4, DebugEventObservationOverflowPolicy.DropNewest);
		ClassicDebugEventDispatcher dispatcher = Register((in observation) =>
		{
			handlerCalls++;
			received = observation;
			handlerNativeThreadId = GetCurrentThreadId();
			handlerManagedThreadId = Environment.CurrentManagedThreadId;
			return DebugEventDecision.PluginOwnsContinuation;
		}, observations);
		NativeThreadWork* work = AllocateWork(new NativeDebugEvent(0x0000_0005, 1234, 5678), 1);

		try
		{
			uint nativeThreadId = RunOnNativeThread(work);

			Assert.Equal(1, handlerCalls);
			Assert.Equal(1, work->ZeroResults);
			Assert.Equal(0, work->NonZeroResults);
			Assert.Equal(nativeThreadId, handlerNativeThreadId);
			Assert.NotEqual(GetCurrentThreadId(), handlerNativeThreadId);
			Assert.NotEqual(Environment.CurrentManagedThreadId, handlerManagedThreadId);
			Assert.Equal(5u, received.EventCode);
			Assert.Equal(1234u, received.ProcessId);
			Assert.Equal(5678u, received.ThreadId);
			Assert.True(observations.TryRead(out DebugEventObservation copied));
			Assert.Equal(received.SequenceNumber, copied.SequenceNumber);
			Assert.Equal(1, dispatcher.UnsupportedContinuationRequestCount);
			Assert.Equal(0, dispatcher.ActiveCallbackCount);
		}
		finally
		{
			NativeMemory.Free(work);
		}

		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	[Trait("Qualification", "Q38")]
	public void Release_drains_callbacks_running_on_native_threads_and_refuses_late_ones()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		using ManualResetEventSlim handlerEntered = new();
		using ManualResetEventSlim allowHandlerToReturn = new();
		using ManualResetEventSlim unregisterEntered = new();
		using ManualResetEventSlim releaseCompleted = new();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		s_unregisterEntered = unregisterEntered;
		int handlerCalls = 0;
		ClassicDebugEventReleaseStatus releaseResult = default;
		ClassicDebugEventDispatcher dispatcher = Register((in _) =>
		{
			Interlocked.Increment(ref handlerCalls);
			handlerEntered.Set();
			allowHandlerToReturn.Wait(cancellationToken);
			return DebugEventDecision.ContinueWithCheatEngine;
		});
		NativeThreadWork* blocked = AllocateWork(new NativeDebugEvent(1, 2, 3), 1);
		NativeThreadWork* lateDuringRelease = AllocateWork(new NativeDebugEvent(4, 5, 6), 3);
		NativeThreadWork* lateAfterRelease = AllocateWork(new NativeDebugEvent(7, 8, 9), 3);
		nint blockedThread = 0;

		try
		{
			blockedThread = StartNativeThread(blocked, out _);
			handlerEntered.Wait(cancellationToken);
			_ = Task.Run(() =>
			{
				releaseResult = dispatcher.TryRelease();
				releaseCompleted.Set();
			}, cancellationToken);
			unregisterEntered.Wait(cancellationToken);

			// Admission is closed: a native callback arriving now never reaches the handler.
			AssertLateCallbacksReturnZeroWithoutTheHandler(lateDuringRelease, ref handlerCalls);
			Assert.False(releaseCompleted.IsSet);
			Assert.Equal(1, dispatcher.ActiveCallbackCount);

			allowHandlerToReturn.Set();
			Assert.Equal(WaitObject0, WaitForSingleObject(blockedThread, NativeThreadTimeoutMilliseconds));
			releaseCompleted.Wait(cancellationToken);
			Assert.Equal(ClassicDebugEventReleaseStatus.Released, releaseResult);
			Assert.Equal(1, blocked->ZeroResults);

			// After the release completed: the thunk is still a valid address, but nothing is admitted.
			AssertLateCallbacksReturnZeroWithoutTheHandler(lateAfterRelease, ref handlerCalls);
		}
		finally
		{
			// The release task starts right after the handler signals that it entered.
			FinishBlockedWork(allowHandlerToReturn, handlerEntered.IsSet ? releaseCompleted : null);
			JoinAndClose(blockedThread);
			FreeWork(blocked, lateDuringRelease, lateAfterRelease);
		}
	}

	[Fact]
	[Trait("Qualification", "Q38")]
	public void Concurrent_native_thread_callbacks_publish_bounded_observations_without_changing_the_native_result()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		const int ThreadCount = 8;
		const int CallbacksPerThread = 200;
		const int Capacity = 16;
		int handlerCalls = 0;
		BoundedDebugEventObservationBuffer observations = new(Capacity, DebugEventObservationOverflowPolicy.DropNewest);
		ClassicDebugEventDispatcher dispatcher = Register((in _) =>
		{
			Interlocked.Increment(ref handlerCalls);
			return DebugEventDecision.ContinueWithCheatEngine;
		}, observations);
		NativeThreadWork*[] work = new NativeThreadWork*[ThreadCount];
		for (int index = 0; index < ThreadCount; index++)
		{
			work[index] = AllocateWork(new NativeDebugEvent((uint) index, 100u + (uint) index, 200u + (uint) index),
				CallbacksPerThread);
		}

		try
		{
			RunConcurrentlyOnNativeThreads(work);

			int total = ThreadCount * CallbacksPerThread;
			Assert.Equal(total, Volatile.Read(ref handlerCalls));
			Assert.Equal(total, Enumerable.Range(0, ThreadCount).Sum(index => work[index]->ZeroResults));
			Assert.All(Enumerable.Range(0, ThreadCount), index => Assert.Equal(0, work[index]->NonZeroResults));
			Assert.Equal(Capacity, observations.Count);
			Assert.Equal(total - Capacity, observations.DroppedObservationCount);
			HashSet<long> sequences = [];
			while (observations.TryRead(out DebugEventObservation observation))
			{
				Assert.True(sequences.Add(observation.SequenceNumber));
				Assert.Equal(observation.EventCode + 100u, observation.ProcessId);
			}

			Assert.Equal(Capacity, sequences.Count);
			Assert.Equal(0, dispatcher.ActiveCallbackCount);
			Assert.Equal(0, dispatcher.CallbackFailureCount);
		}
		finally
		{
			FreeWork(work);
		}

		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
	}

	[Fact]
	public void Second_registration_while_one_is_active_is_refused_before_calling_the_host()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		ClassicDebugEventDispatcher first = Register(static (in _) => DebugEventDecision.ContinueWithCheatEngine);
		Assert.Equal(1, s_registerCalls);

		ClassicDebugEventRegistrationStatus status = TryRegisterWithHost(out ClassicDebugEventDispatcher? second);

		Assert.Equal(ClassicDebugEventRegistrationStatus.AnotherRegistrationIsActive, status);
		Assert.Null(second);
		Assert.Equal(1, s_registerCalls);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, first.TryRelease());
	}

	[Fact]
	public void Negative_function_id_is_a_host_refusal_that_frees_the_record_and_allows_a_new_registration()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		s_functionIdToReturn = -1;

		ClassicDebugEventRegistrationStatus refused = TryRegisterWithHost(out ClassicDebugEventDispatcher? none);

		Assert.Equal(ClassicDebugEventRegistrationStatus.HostRejectedRegistration, refused);
		Assert.Null(none);
		Assert.Equal(1, s_registerCalls);
		Assert.Equal(0, s_unregisterCalls);
		// The host kept the thunk address it was given; after the refusal it no longer reaches any handler.
		NativeDebugEvent stray = new(1, 2, 3);
		Assert.Equal(0, Invoke(&stray));

		s_functionIdToReturn = DefaultFunctionId;
		int handlerCalls = 0;
		ClassicDebugEventDispatcher dispatcher = Register((in _) =>
		{
			handlerCalls++;
			return DebugEventDecision.ContinueWithCheatEngine;
		});
		NativeDebugEvent nativeEvent = new(4, 5, 6);
		Assert.Equal(0, Invoke(&nativeEvent));
		Assert.Equal(1, handlerCalls);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
		Assert.Equal(DefaultFunctionId, s_lastUnregisteredFunctionId);
	}

	[Fact]
	public void Function_id_zero_is_a_valid_identifier_passed_back_on_unregister()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		s_functionIdToReturn = 0;

		ClassicDebugEventDispatcher dispatcher = Register(static (in _) => DebugEventDecision.ContinueWithCheatEngine);

		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
		Assert.Equal(1, s_unregisterCalls);
		Assert.Equal(0, s_lastUnregisteredFunctionId);
	}

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(false, false)]
	public void Missing_register_or_unregister_slot_is_refused_before_any_host_call(bool withRegister, bool withUnregister)
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		s_registerCalls = 0;
		ExportedFunctionsPrefix exports = default;
		if (withRegister)
		{
			exports.RegisterFunction = &RegisterFunction;
		}

		if (withUnregister)
		{
			exports.UnregisterFunction = &UnregisterFunction;
		}

		ClassicDebugEventRegistrationStatus status = ClassicDebugEventDispatcher.TryRegister(in exports, 77,
			static (in _) => DebugEventDecision.ContinueWithCheatEngine, null, TestContext.Current.CancellationToken,
			out ClassicDebugEventDispatcher? dispatcher);

		Assert.Equal(ClassicDebugEventRegistrationStatus.MissingHostFunction, status);
		Assert.Null(dispatcher);
		Assert.Equal(0, s_registerCalls);
		Assert.Equal(0, s_unregisterCalls);
	}

	[Fact]
	public void After_release_a_late_callback_returns_zero_without_reaching_the_handler()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		int handlerCalls = 0;
		ClassicDebugEventDispatcher dispatcher = Register((in _) =>
		{
			handlerCalls++;
			return DebugEventDecision.ContinueWithCheatEngine;
		});
		delegate* unmanaged[Stdcall]<void*, int> retainedByHost = s_callback;

		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
		NativeDebugEvent late = new(1, 2, 3);
		Assert.Equal(0, retainedByHost(&late));
		Assert.Equal(0, handlerCalls);
		Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
		Assert.Equal(1, s_unregisterCalls);
	}

	private ClassicDebugEventDispatcher Register(
		DebugEventDecisionHandler handler,
		BoundedDebugEventObservationBuffer? observations = null)
	{
		return RegisterWithCancellation(handler, observations, TestContext.Current.CancellationToken);
	}

	private ClassicDebugEventDispatcher RegisterWithCancellation(
		DebugEventDecisionHandler handler,
		BoundedDebugEventObservationBuffer? observations,
		CancellationToken cancellationToken)
	{
		s_unregisterSucceeds = true;
		s_unregisterCalls = 0;
		s_callback = null;
		ExportedFunctionsPrefix exports = default;
		exports.RegisterFunction = &RegisterFunction;
		exports.UnregisterFunction = &UnregisterFunction;

		ClassicDebugEventRegistrationStatus status = ClassicDebugEventDispatcher.TryRegister(in exports, 77, handler,
			observations, cancellationToken,
			out ClassicDebugEventDispatcher? dispatcher);

		Assert.Equal(ClassicDebugEventRegistrationStatus.Registered, status);
		Assert.NotNull(dispatcher);
		Assert.NotEqual(0, (nint) s_callback);
		_dispatcher = dispatcher;
		return dispatcher;
	}

	private ClassicDebugEventRegistrationStatus TryRegisterWithHost(out ClassicDebugEventDispatcher? dispatcher)
	{
		ExportedFunctionsPrefix exports = default;
		exports.RegisterFunction = &RegisterFunction;
		exports.UnregisterFunction = &UnregisterFunction;
		ClassicDebugEventRegistrationStatus status = ClassicDebugEventDispatcher.TryRegister(in exports, 77,
			static (in _) => DebugEventDecision.ContinueWithCheatEngine, null, TestContext.Current.CancellationToken,
			out dispatcher);
		if (dispatcher is not null)
		{
			_dispatcher = dispatcher;
		}

		return status;
	}

	private static NativeThreadWork* AllocateWork(NativeDebugEvent nativeEvent, int iterations)
	{
		NativeThreadWork* work = (NativeThreadWork*) NativeMemory.AllocZeroed((nuint) sizeof(NativeThreadWork));
		work->Event = nativeEvent;
		work->Iterations = iterations;
		return work;
	}

	/// <summary>Creates a Win32 thread that calls the registered thunk; the CLR has never seen this thread.</summary>
	private static nint StartNativeThread(NativeThreadWork* work, out uint threadId)
	{
		uint id = 0;
		nint handle = CreateThread(0, 0, &RunCallbacksOnNativeThread, work, 0, &id);
		Assert.NotEqual(0, handle);
		threadId = id;
		return handle;
	}

	/// <summary>Runs <paramref name="work" /> on a new native thread to completion and returns that thread's id.</summary>
	private static uint RunOnNativeThread(NativeThreadWork* work)
	{
		nint handle = StartNativeThread(work, out uint threadId);
		try
		{
			Assert.Equal(WaitObject0, WaitForSingleObject(handle, NativeThreadTimeoutMilliseconds));
		}
		finally
		{
			_ = CloseHandle(handle);
		}

		return threadId;
	}

	/// <summary>Starts one native thread per work item, all at once, and waits for every one of them.</summary>
	private static void RunConcurrentlyOnNativeThreads(NativeThreadWork*[] work)
	{
		nint[] threads = new nint[work.Length];
		try
		{
			for (int index = 0; index < work.Length; index++)
			{
				threads[index] = StartNativeThread(work[index], out _);
			}

			foreach (nint thread in threads)
			{
				Assert.Equal(WaitObject0, WaitForSingleObject(thread, NativeThreadTimeoutMilliseconds));
			}
		}
		finally
		{
			foreach (nint thread in threads)
			{
				JoinAndClose(thread);
			}
		}
	}

	/// <summary>Runs <paramref name="late" /> on a new native thread; exactly one earlier callback reached the handler.</summary>
	private static void AssertLateCallbacksReturnZeroWithoutTheHandler(NativeThreadWork* late, ref int handlerCalls)
	{
		RunOnNativeThread(late);
		Assert.Equal(late->Iterations, late->ZeroResults);
		Assert.Equal(1, Volatile.Read(ref handlerCalls));
	}

	/// <summary>
	///     Cleanup of a test that blocks a handler and releases on a <c>Task.Run</c> thread: lets the handler return, then
	///     waits (bounded) for each started completion. After an early assertion failure, a release still running would
	///     call <see cref="UnregisterFunction" />, which sets the test's <c>using</c>-scoped unregister event from an
	///     <c>[UnmanagedCallersOnly]</c> frame, where an <see cref="ObjectDisposedException" /> ends the process; and a
	///     callback still running would read event memory the test is about to free.
	/// </summary>
	/// <param name="allowHandlerToReturn">The event the blocked handler waits for.</param>
	/// <param name="completions">The completion events of the work started so far; <see langword="null" /> when not started.</param>
	private static void FinishBlockedWork(ManualResetEventSlim allowHandlerToReturn,
		params ReadOnlySpan<ManualResetEventSlim?> completions)
	{
		allowHandlerToReturn.Set();
		foreach (ManualResetEventSlim? completion in completions)
		{
			_ = completion?.Wait(ReleaseTaskTimeout, CancellationToken.None);
		}
	}

	private static void JoinAndClose(nint thread)
	{
		if (thread != 0)
		{
			_ = WaitForSingleObject(thread, NativeThreadTimeoutMilliseconds);
			_ = CloseHandle(thread);
		}
	}

	private static void FreeWork(params NativeThreadWork*[] work)
	{
		foreach (NativeThreadWork* item in work)
		{
			NativeMemory.Free(item);
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static uint RunCallbacksOnNativeThread(void* parameter)
	{
		NativeThreadWork* work = (NativeThreadWork*) parameter;
		for (int iteration = 0; iteration < work->Iterations; iteration++)
		{
			if (s_callback(&work->Event) == 0)
			{
				work->ZeroResults++;
			}
			else
			{
				work->NonZeroResults++;
			}
		}

		return 0;
	}

	[LibraryImport("kernel32.dll")]
	private static partial nint CreateThread(nint threadAttributes, nuint stackSize,
		delegate* unmanaged[Stdcall]<void*, uint> startAddress, void* parameter, uint creationFlags, uint* threadId);

	[LibraryImport("kernel32.dll")]
	private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

	[LibraryImport("kernel32.dll")]
	private static partial int CloseHandle(nint handle);

	[LibraryImport("kernel32.dll")]
	private static partial uint GetCurrentThreadId();

	private static int Invoke(NativeDebugEvent* nativeEvent)
	{
		return s_callback(nativeEvent);
	}

	private static int InvokeValue(NativeDebugEvent nativeEvent)
	{
		return Invoke(&nativeEvent);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int RegisterFunction(int pluginId, PluginType functionType, void* initialization)
	{
		Assert.Equal(77, pluginId);
		Assert.Equal(PluginType.OnDebugEvent, functionType);
		Assert.NotEqual(0, (nint) initialization);
		s_registerCalls++;
		s_callback = ((DebugEventPluginInit*) initialization)->Callback;
		return s_functionIdToReturn;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static Bool32 UnregisterFunction(int pluginId, int functionId)
	{
		Assert.Equal(77, pluginId);
		Assert.Equal(s_functionIdToReturn, functionId);
		s_lastUnregisteredFunctionId = functionId;
		s_unregisterCalls++;
		s_unregisterEntered?.Set();
		return s_unregisterSucceeds;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct NativeDebugEvent(uint eventCode, uint processId, uint threadId)
	{
		public readonly uint EventCode = eventCode;
		public readonly uint ProcessId = processId;
		public readonly uint ThreadId = threadId;
	}

	/// <summary>What a native thread does and what it observed; written only by that thread until it ends.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct NativeThreadWork
	{
		public NativeDebugEvent Event;
		public int Iterations;
		public int ZeroResults;
		public int NonZeroResults;
	}
}
