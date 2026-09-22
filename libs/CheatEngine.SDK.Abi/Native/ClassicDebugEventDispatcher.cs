using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Owns one classic type-2 callback registration for a loaded SDK assembly. This is deliberately internal until a
///     live CE profile qualifies the host table's registration lifetime and continuation slot.
/// </summary>
/// <remarks>
///     <para>
///         A type-2 callback receives no context argument, so this dispatcher supports one active registration per
///         loaded assembly. The static unmanaged thunk is AOT-safe, while this instance holds the managed handler root
///         from before registration through confirmed unregistration and drain.
///     </para>
///     <para>
///         The current contract always returns zero to leave continuation with Cheat Engine. A handler can request
///         <see cref="DebugEventDecision.PluginOwnsContinuation" />, but that request is rejected because the hookable
///         <c>ContinueDebugEvent</c> cell is not yet a qualified SDK ABI surface.
///     </para>
/// </remarks>
internal sealed unsafe class ClassicDebugEventDispatcher : IDisposable
{
	private static readonly Lock SRegistrationGate = new();
	private static ClassicDebugEventDispatcher? s_active;
	private static long s_nextSequenceNumber;

	[ThreadStatic] private static ClassicDebugEventDispatcher? t_dispatcher;

	private readonly ManualResetEventSlim _callbacksDrained = new(true);
	private readonly CancellationToken _cancellationToken;

	private readonly Lock _gate = new();
	private readonly int _pluginId;
	private readonly delegate* unmanaged[Stdcall]<int, PluginType, void*, int> _registerFunction;
	private readonly delegate* unmanaged[Stdcall]<int, int, Bool32> _unregisterFunction;
	private bool _acceptingCallbacks;
	private int _activeCallbacks;
	private long _callbackFailureCount;
	private int _functionId;
	private DebugEventDecisionHandler? _handler;
	private DebugEventPluginInit* _initialization;
	private BoundedDebugEventObservationBuffer? _observations;
	private bool _releaseInProgress;
	private bool _released;
	private long _unsupportedContinuationRequestCount;

	private ClassicDebugEventDispatcher(
		in ExportedFunctionsPrefix exports,
		int pluginId,
		DebugEventDecisionHandler handler,
		BoundedDebugEventObservationBuffer? observations,
		CancellationToken cancellationToken)
	{
		_registerFunction = exports.RegisterFunction;
		_unregisterFunction = exports.UnregisterFunction;
		_pluginId = pluginId;
		_handler = handler;
		_observations = observations;
		_cancellationToken = cancellationToken;
		_acceptingCallbacks = true;
	}

	/// <summary>Gets the number of caught managed handler failures.</summary>
	internal long CallbackFailureCount => Interlocked.Read(ref _callbackFailureCount);

	/// <summary>Gets the number of unsupported plugin-continuation requests rejected to CE continuation.</summary>
	internal long UnsupportedContinuationRequestCount => Interlocked.Read(ref _unsupportedContinuationRequestCount);

	/// <summary>Gets the number of callback invocations admitted before the close boundary.</summary>
	internal int ActiveCallbackCount
	{
		get
		{
			lock (_gate)
			{
				return _activeCallbacks;
			}
		}
	}

	/// <summary>Attempts the same conservative release sequence as an explicit owner teardown.</summary>
	public void Dispose()
	{
		_ = TryRelease();
	}

	/// <summary>
	///     Installs the static callback thunk through the direct, qualified prefix slots.
	/// </summary>
	internal static ClassicDebugEventRegistrationStatus TryRegister(
		in ExportedFunctionsPrefix exports,
		int pluginId,
		DebugEventDecisionHandler handler,
		BoundedDebugEventObservationBuffer? observations,
		CancellationToken cancellationToken,
		out ClassicDebugEventDispatcher? dispatcher)
	{
		ArgumentNullException.ThrowIfNull(handler);
		dispatcher = null;

		if (!AbiArchitecture.IsSupported)
		{
			return ClassicDebugEventRegistrationStatus.UnsupportedArchitecture;
		}

		if (exports.RegisterFunction is null || exports.UnregisterFunction is null)
		{
			return ClassicDebugEventRegistrationStatus.MissingHostFunction;
		}

		ClassicDebugEventDispatcher created = new(in exports, pluginId, handler, observations, cancellationToken);
		lock (SRegistrationGate)
		{
			if (s_active is not null)
			{
				return ClassicDebugEventRegistrationStatus.AnotherRegistrationIsActive;
			}

			// Publish the strong root before registration because a native host is permitted to call its callback
			// synchronously while RegisterFunction is still on the stack.
			s_active = created;
		}

		try
		{
			created._initialization = (DebugEventPluginInit*) NativeMemory.Alloc((nuint) sizeof(DebugEventPluginInit));
			*created._initialization = new DebugEventPluginInit { Callback = &DispatchUnmanaged };
			int functionId = created._registerFunction(pluginId, PluginType.OnDebugEvent, created._initialization);
			if (functionId < 0)
			{
				created.AbandonFailedRegistration();
				return ClassicDebugEventRegistrationStatus.HostRejectedRegistration;
			}

			created._functionId = functionId;
			dispatcher = created;
			return ClassicDebugEventRegistrationStatus.Registered;
		}
		catch (Exception)
		{
			created.AbandonFailedRegistration();
			return ClassicDebugEventRegistrationStatus.RegistrationFault;
		}
	}

	/// <summary>
	///     Closes callback admission, unregisters through the host, drains admitted callbacks, and only then clears the
	///     managed root and native registration record.
	/// </summary>
	internal ClassicDebugEventReleaseStatus TryRelease()
	{
		if (ReferenceEquals(t_dispatcher, this))
		{
			return ClassicDebugEventReleaseStatus.CallbackIsExecuting;
		}

		lock (_gate)
		{
			if (_released)
			{
				return ClassicDebugEventReleaseStatus.Released;
			}

			if (_releaseInProgress)
			{
				return ClassicDebugEventReleaseStatus.ReleaseInProgress;
			}

			_acceptingCallbacks = false;
			_releaseInProgress = true;
		}

		if (!TryUnregister())
		{
			return ClassicDebugEventReleaseStatus.UnregisterUnconfirmed;
		}

		CompleteRelease();
		return ClassicDebugEventReleaseStatus.Released;
	}

	private bool TryUnregister()
	{
		try
		{
			if (_unregisterFunction(_pluginId, _functionId).IsTrue)
			{
				return true;
			}
		}
		catch (Exception)
		{
			// The host can fail without throwing or can throw from the unmanaged call.
		}

		EndUnconfirmedReleaseAttempt();
		return false;
	}

	private void CompleteRelease()
	{
		_callbacksDrained.Wait();
		lock (SRegistrationGate)
		{
			if (ReferenceEquals(s_active, this))
			{
				s_active = null;
			}
		}

		DebugEventPluginInit* initialization;
		lock (_gate)
		{
			initialization = _initialization;
			_initialization = null;
			_handler = null;
			_observations = null;
			_released = true;
			_releaseInProgress = false;
		}

		if (initialization is not null)
		{
			NativeMemory.Free(initialization);
		}

		_callbacksDrained.Dispose();
	}

	private static int Dispatch(void* nativeEvent)
	{
		// Exact native value: zero asks Cheat Engine to handle and continue the event. Every rejection/failure below
		// deliberately returns this fallback, so neither a queue nor user code can claim continuation ownership.
		if (nativeEvent is null)
		{
			return 0;
		}

		ClassicDebugEventDispatcher? dispatcher;
		lock (SRegistrationGate)
		{
			dispatcher = s_active;
		}

		if (dispatcher is null || !dispatcher.TryEnterCallback(out DebugEventDecisionHandler handler,
			    out BoundedDebugEventObservationBuffer? observations))
		{
			return 0;
		}

		ClassicDebugEventDispatcher? previous = t_dispatcher;
		t_dispatcher = dispatcher;
		try
		{
			return InvokeHandler(dispatcher, handler, observations, nativeEvent);
		}
		catch (Exception)
		{
			// Includes defensive failure while copying or publishing. Nothing escapes a native stdcall frame.
			Interlocked.Increment(ref dispatcher._callbackFailureCount);
			return 0;
		}
		finally
		{
			t_dispatcher = previous;
			dispatcher.ExitCallback();
		}
	}

	private static int InvokeHandler(
		ClassicDebugEventDispatcher dispatcher,
		DebugEventDecisionHandler handler,
		BoundedDebugEventObservationBuffer? observations,
		void* nativeEvent)
	{
		DebugEventHeader header = Unsafe.ReadUnaligned<DebugEventHeader>(nativeEvent);
		DebugEventObservation observation = new(
			Interlocked.Increment(ref s_nextSequenceNumber),
			header.EventCode,
			header.ProcessId,
			header.ThreadId);

		// This is a bounded copy, never a continuation path. It cannot call a consumer or await work.
		observations?.TryPublish(in observation);

		DebugEventDecision decision;
		try
		{
			decision = handler(in observation);
		}
		catch (Exception)
		{
			Interlocked.Increment(ref dispatcher._callbackFailureCount);
			return 0;
		}

		if (decision is not DebugEventDecision.ContinueWithCheatEngine)
		{
			Interlocked.Increment(ref dispatcher._unsupportedContinuationRequestCount);
		}

		return 0;
	}

	private bool TryEnterCallback(
		out DebugEventDecisionHandler handler,
		out BoundedDebugEventObservationBuffer? observations)
	{
		lock (_gate)
		{
			if (!_acceptingCallbacks || _cancellationToken.IsCancellationRequested || _handler is null)
			{
				handler = null!;
				observations = null;
				return false;
			}

			if (_activeCallbacks == 0)
			{
				_callbacksDrained.Reset();
			}

			checked
			{
				_activeCallbacks++;
			}

			handler = _handler;
			observations = _observations;
			return true;
		}
	}

	private void ExitCallback()
	{
		lock (_gate)
		{
			// This cannot occur through the SDK thunk, but an unmanaged callback must never throw across the boundary.
			if (_activeCallbacks <= 0)
			{
				return;
			}

			_activeCallbacks--;
			if (_activeCallbacks == 0)
			{
				_callbacksDrained.Set();
			}
		}
	}

	private void AbandonFailedRegistration()
	{
		DebugEventPluginInit* initialization;
		lock (_gate)
		{
			_acceptingCallbacks = false;
			initialization = _initialization;
			_initialization = null;
			_handler = null;
			_observations = null;
			_released = true;
			_releaseInProgress = false;
		}

		lock (SRegistrationGate)
		{
			if (ReferenceEquals(s_active, this))
			{
				s_active = null;
			}
		}

		if (initialization is not null)
		{
			NativeMemory.Free(initialization);
		}

		_callbacksDrained.Dispose();
	}

	private void EndUnconfirmedReleaseAttempt()
	{
		lock (_gate)
		{
			_releaseInProgress = false;
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int DispatchUnmanaged(void* nativeEvent)
	{
		// Kept as a separately attributed method so the function-pointer declaration on DebugEventPluginInit remains
		// the only exported ABI shape. Dispatch is called through the native-compatible address below.
		return Dispatch(nativeEvent);
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct DebugEventHeader(uint eventCode, uint processId, uint threadId)
	{
		public readonly uint EventCode = eventCode;
		public readonly uint ProcessId = processId;
		public readonly uint ThreadId = threadId;
	}
}
