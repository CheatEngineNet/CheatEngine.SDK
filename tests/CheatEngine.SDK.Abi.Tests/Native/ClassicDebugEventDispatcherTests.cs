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
public sealed unsafe class ClassicDebugEventDispatcherTests : IDisposable
{
    private ClassicDebugEventDispatcher? _dispatcher;

    [Fact]
    public void Callback_runs_the_synchronous_handler_with_a_scalar_copy_and_leaves_continuation_to_CheatEngine()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        DebugEventObservation received = default;
        var handlerCalls = 0;
        var dispatcher = Register((in DebugEventObservation observation) =>
        {
            handlerCalls++;
            received = observation;
            return DebugEventDecision.ContinueWithCheatEngine;
        });
        NativeDebugEvent nativeEvent = new(0x0000_0006, 101, 202);

        var result = Invoke(&nativeEvent);

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
        var dispatcher = Register(static (in DebugEventObservation _) => DebugEventDecision.ContinueWithCheatEngine,
            observations);
        NativeDebugEvent nativeEvent = new(3, 404, 505);

        Assert.Equal(0, Invoke(&nativeEvent));
        nativeEvent = new(99, 0, 0);

        Assert.True(observations.TryRead(out var copied));
        Assert.Equal(3u, copied.EventCode);
        Assert.Equal(404u, copied.ProcessId);
        Assert.Equal(505u, copied.ThreadId);
        Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
    }

    [Fact]
    public void Plugin_owned_continuation_request_is_rejected_to_the_current_CheatEngine_owned_fallback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        var dispatcher = Register(static (in DebugEventObservation _) => DebugEventDecision.PluginOwnsContinuation);
        NativeDebugEvent nativeEvent = new(1, 2, 3);

        Assert.Equal(0, Invoke(&nativeEvent));
        Assert.Equal(1, dispatcher.UnsupportedContinuationRequestCount);
        Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
    }

    [Fact]
    public void Callback_exception_is_contained_and_returns_the_CheatEngine_owned_fallback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        var dispatcher = Register(static (in DebugEventObservation _) => throw new InvalidOperationException("boom"));
        NativeDebugEvent nativeEvent = new(1, 2, 3);

        var result = 1;
        var exception = Record.Exception(() => { result = InvokeValue(nativeEvent); });

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
        var handlerCalls = 0;
        var dispatcher = RegisterWithCancellation((in DebugEventObservation _) =>
        {
            handlerCalls++;
            return DebugEventDecision.ContinueWithCheatEngine;
        }, observations: null, cancellationToken: cancellation.Token);
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
        var dispatcher = Register(static (in DebugEventObservation _) => DebugEventDecision.ContinueWithCheatEngine,
            observations);
        NativeDebugEvent first = new(1, 2, 3);
        NativeDebugEvent second = new(2, 3, 4);

        Assert.Equal(0, Invoke(&first));
        Assert.Equal(0, Invoke(&second));
        Assert.Equal(1, observations.DroppedObservationCount);
        Assert.True(observations.TryRead(out var retained));
        Assert.Equal(1u, retained.EventCode);
        Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
    }

    [Fact]
    public void Release_closes_admission_before_unregistration_failure_and_keeps_the_root_for_retry()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        var handlerCalls = 0;
        var dispatcher = Register((in DebugEventObservation _) =>
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
        var cancellationToken = TestContext.Current.CancellationToken;
        s_unregisterEntered = unregisterEntered;
        var handlerCalls = 0;
        var dispatcher = Register((in DebugEventObservation _) =>
        {
            handlerCalls++;
            handlerEntered.Set();
            allowHandlerToReturn.Wait(cancellationToken);
            return DebugEventDecision.ContinueWithCheatEngine;
        });
        var eventMemory = (NativeDebugEvent*)NativeMemory.Alloc((nuint)sizeof(NativeDebugEvent));
        *eventMemory = new NativeDebugEvent(1, 2, 3);
        var callbackResult = 1;
        ClassicDebugEventReleaseStatus releaseResult = default;

        try
        {
            var eventAddress = (nint)eventMemory;
            _ = Task.Run(() =>
            {
                callbackResult = Invoke((NativeDebugEvent*)eventAddress);
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
            NativeMemory.Free(eventMemory);
        }
    }

    [Fact]
    public void Release_reentered_from_a_handler_is_refused_without_deadlocking_or_freeing_the_target()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        ClassicDebugEventDispatcher? dispatcher = null;
        ClassicDebugEventReleaseStatus status = default;
        dispatcher = Register((in DebugEventObservation _) =>
        {
            status = dispatcher!.TryRelease();
            return DebugEventDecision.ContinueWithCheatEngine;
        });
        NativeDebugEvent nativeEvent = new(1, 2, 3);

        Assert.Equal(0, Invoke(&nativeEvent));
        Assert.Equal(ClassicDebugEventReleaseStatus.CallbackIsExecuting, status);
        Assert.Equal(ClassicDebugEventReleaseStatus.Released, dispatcher.TryRelease());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        s_unregisterSucceeds = true;
        s_unregisterEntered = null;
        _dispatcher?.TryRelease();
        _dispatcher = null;
        s_callback = null;
        s_unregisterCalls = 0;
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

        var status = ClassicDebugEventDispatcher.TryRegister(in exports, 77, handler, observations, cancellationToken,
            out var dispatcher);

        Assert.Equal(ClassicDebugEventRegistrationStatus.Registered, status);
        Assert.NotNull(dispatcher);
        Assert.NotEqual((nint)0, (nint)s_callback);
        _dispatcher = dispatcher;
        return dispatcher;
    }

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
        Assert.NotEqual((nint)0, (nint)initialization);
        s_callback = ((DebugEventPluginInit*)initialization)->Callback;
        return 901;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 UnregisterFunction(int pluginId, int functionId)
    {
        Assert.Equal(77, pluginId);
        Assert.Equal(901, functionId);
        s_unregisterCalls++;
        s_unregisterEntered?.Set();
        return s_unregisterSucceeds;
    }

    private static delegate* unmanaged[Stdcall]<void*, int> s_callback;
    private static int s_unregisterCalls;
    private static bool s_unregisterSucceeds;
    private static ManualResetEventSlim? s_unregisterEntered;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeDebugEvent(uint eventCode, uint processId, uint threadId)
    {
        public readonly uint EventCode = eventCode;
        public readonly uint ProcessId = processId;
        public readonly uint ThreadId = threadId;
    }
}
