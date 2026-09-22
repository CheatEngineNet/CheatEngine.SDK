using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace LiveProbe;

// All live observations are process-local and are deliberately kept out of the SDK runtime.  The lock only protects
// report fields; Lua is never called while it is held.
internal static unsafe class LiveProbeState
{
	private const uint TailCanary = 0x7A_51_CE_77U;
	private static readonly Lock Gate = new();
	private static BootstrapObservation s_bootstrap;
	private static AuthorizationDecision s_bootstrapAuthorization = AuthorizationDecision.Denied("Not evaluated.");
	private static AuthorizationDecision s_enableAuthorization = AuthorizationDecision.Denied("Not enabled.");
	private static bool s_targetMatchesCe;
	private static string s_targetMatchFailure = "Not checked.";
	private static SynchronizeObservation s_synchronize = SynchronizeObservation.NotStarted;
	private static LuaThreadObservation s_luaThread = LuaThreadObservation.NotStarted;
	private static ResetObservation s_reset = ResetObservation.NotStarted;
	private static LuaRef? s_resetReference;
	private static LuaCallback<CallbackCounter>? s_callback;
	private static CallbackCounter? s_callbackCounter;
	private static int s_luaProbeSerial;

	internal static void CaptureBootstrap(nint initRecord, int opaqueHostArgument)
	{
		lock (Gate)
		{
			s_bootstrap = s_bootstrap with
			{
				Calls = checked(s_bootstrap.Calls + 1),
				InitRecord = initRecord,
				OpaqueArgument = opaqueHostArgument,
				Captured = true
			};
		}
	}

	internal static void TryWriteTailCanaryAfterPackedRecord(nint initRecord, int bootstrapResult)
	{
		if (bootstrapResult != ManagedEntryPoint.Success || initRecord == 0)
		{
			return;
		}

		// This is the one deliberately high-risk probe. It does not run unless the exact CE executable, an unexpired
		// operator acknowledgement, and a live hash-verified disposable target all pass. A tail value surviving does not
		// by itself prove allocation capacity; the raw before/after records are evidence for manual review.
		AuthorizationDecision authorization = LiveProbeAuthorization.Evaluate();
		lock (Gate)
		{
			s_bootstrapAuthorization = authorization;
		}

		if (!authorization.IsAllowed)
		{
			return;
		}

		try
		{
			byte* tail = (byte*) initRecord + sizeof(PluginInitRecord);
			uint before = ReadUnalignedUInt32(tail);
			WriteUnalignedUInt32(tail, TailCanary);
			lock (Gate)
			{
				s_bootstrap = s_bootstrap with
				{
					TailReadBeforeWrite = before,
					TailCanaryWritten = true,
					TailWriteCount = checked(s_bootstrap.TailWriteCount + 1)
				};
			}
		}
		catch (Exception exception)
		{
			lock (Gate)
			{
				s_bootstrap = s_bootstrap with
				{
					TailFailure = Describe(exception)
				};
			}

			HostLog.Write(HostLogLevel.Error, "CE 7.7 live-probe tail canary failed.", exception);
		}
	}

	internal static void ValidateAfterEnable()
	{
		ValidateAfterEnable(LiveProbeAuthorization.Evaluate, ProbeHostGlobals.GetOpenedProcessId);
	}

	// The delegates keep the refresh decision deterministic in unit tests. They are internal to this manually loaded
	// harness; the only operator-facing path always supplies the authorization evaluator and CE PID reader above.
	internal static void ValidateAfterEnable(Func<AuthorizationDecision> evaluateAuthorization,
		Func<long> getOpenedProcessId)
	{
		RecordRuntimeAuthorization(EvaluateRuntimeAuthorization(evaluateAuthorization, getOpenedProcessId));
	}

	private static RuntimeAuthorization EvaluateRuntimeAuthorization(Func<AuthorizationDecision> evaluateAuthorization,
		Func<long> getOpenedProcessId)
	{
		AuthorizationDecision authorization = evaluateAuthorization();
		bool targetMatchesCe = false;
		string targetFailure = authorization.IsAllowed
			? "CE target attachment is not yet checked."
			: authorization.Reason;

		if (authorization.IsAllowed)
		{
			try
			{
				long openedProcess = getOpenedProcessId();
				targetMatchesCe = openedProcess == authorization.TargetProcessId;
				targetFailure = targetMatchesCe
					? "CE's opened process matches the disposable-target manifest."
					: string.Create(CultureInfo.InvariantCulture,
						$"CE reports opened process {openedProcess}, not manifest process {authorization.TargetProcessId}.");
			}
			catch (Exception exception)
			{
				targetFailure = "Could not read CE getOpenedProcessID(): " + Describe(exception);
			}
		}

		return new RuntimeAuthorization(authorization, targetMatchesCe, targetFailure);
	}

	private static void RecordRuntimeAuthorization(RuntimeAuthorization runtimeAuthorization)
	{
		lock (Gate)
		{
			s_enableAuthorization = runtimeAuthorization.Authorization;
			s_targetMatchesCe = runtimeAuthorization.TargetMatchesCe;
			s_targetMatchFailure = runtimeAuthorization.TargetMatchFailure;
		}
	}

	internal static void RecordDisable()
	{
		CallbackCounter? counter;
		LuaCallback<CallbackCounter>? callback;
		lock (Gate)
		{
			counter = s_callbackCounter;
			callback = s_callback;
		}

		if (callback is null)
		{
			return;
		}

		HostLog.Write(HostLogLevel.Information, string.Create(CultureInfo.InvariantCulture,
			$"CE 7.7 live probe: callback shutdown handoff; IsReleased before LuaRuntime.Detach={callback.IsReleased}, " +
			$"managed calls={counter?.Calls ?? 0}."));
	}

	internal static string GetStatus()
	{
		lock (Gate)
		{
			StringBuilder builder = new(1024);
			builder.Append("CE 7.7 live probe status; bootstrapCalls=").Append(s_bootstrap.Calls)
				.Append(", bootstrapAddress=0x")
				.Append(s_bootstrap.InitRecord.ToString("X", CultureInfo.InvariantCulture))
				.Append(", opaqueSecondInt=").Append(s_bootstrap.OpaqueArgument)
				.Append(" (raw; no size/version meaning assigned)")
				.Append(", tailCanaryWritten=").Append(s_bootstrap.TailCanaryWritten)
				.Append(", tailWrites=").Append(s_bootstrap.TailWriteCount);

			if (s_bootstrap.TailCanaryWritten)
			{
				builder.Append(", tailBefore=0x")
					.Append(s_bootstrap.TailReadBeforeWrite.ToString("X8", CultureInfo.InvariantCulture))
					.Append(", expectedPreviousCanary=0x")
					.Append(TailCanary.ToString("X8", CultureInfo.InvariantCulture));
			}

			if (!string.IsNullOrEmpty(s_bootstrap.TailFailure))
			{
				builder.Append(", tailFailure=").Append(s_bootstrap.TailFailure);
			}

			builder.Append(". Bootstrap gate: ").Append(s_bootstrapAuthorization.IsAllowed ? "allowed" : "denied")
				.Append("; ").Append(s_bootstrapAuthorization.Reason)
				.Append(". Runtime gate: ").Append(IsRuntimeProbeAllowedUnsafe() ? "allowed" : "denied")
				.Append("; ").Append(s_targetMatchFailure)
				.Append(". Synchronize: ").Append(s_synchronize.ToDisplayString())
				.Append(". Lua threads: ").Append(s_luaThread.ToDisplayString())
				.Append(". Reset: ").Append(s_reset.ToDisplayString());

			if (s_callback is not null)
			{
				builder.Append(". Callback: prepared=true, released=").Append(s_callback.IsReleased)
					.Append(", managedCalls=").Append(s_callbackCounter?.Calls ?? 0);
			}

			return builder.ToString();
		}
	}

	internal static string CaptureHostProfile()
	{
		return CaptureHostProfile(LiveProbeAuthorization.Evaluate, ProbeHostGlobals.GetOpenedProcessId,
			HostProfileObservation.Capture);
	}

	// A profile is evidence only for the instant it is captured. Revalidate the manifest, target image and CE's opened
	// PID immediately beforehand rather than accepting the enable-time diagnostic snapshot. This fresh decision stays
	// local to capture; every other protected command performs its own fresh check as well.
	internal static string CaptureHostProfile(Func<AuthorizationDecision> evaluateAuthorization,
		Func<long> getOpenedProcessId, Func<AuthorizationDecision, string> capture)
	{
		RuntimeAuthorization runtimeAuthorization =
			EvaluateRuntimeAuthorization(evaluateAuthorization, getOpenedProcessId);
		if (!runtimeAuthorization.IsAllowed)
		{
			return "Live probe denied: " + runtimeAuthorization.Denial;
		}

		return capture(runtimeAuthorization.Authorization);
	}

	internal static string BeginSynchronizeProbe()
	{
		if (!TryRequireRuntimeAuthorization(out string denied))
		{
			return denied;
		}

		lock (Gate)
		{
			if (s_synchronize.IsRunning)
			{
				return "Synchronize probe is already running; call ce77_live_probe_synchronize_status().";
			}

			s_synchronize = SynchronizeObservation.Started;
		}

		Thread thread = new(RunSynchronizeProbe)
		{
			IsBackground = true,
			Name = "CheatEngine.SDK CE77 synchronize probe"
		};
		thread.Start();
		return "Synchronize probe started. Do not block the CE GUI thread; poll ce77_live_probe_synchronize_status().";
	}

	internal static string GetSynchronizeStatus()
	{
		lock (Gate)
		{
			return s_synchronize.ToDisplayString();
		}
	}

	internal static string BeginLuaThreadProbe()
	{
		if (!TryRequireRuntimeAuthorization(out string denied))
		{
			return denied;
		}

		LuaState state = LuaRuntime.AcquireState();
		int serial = Interlocked.Increment(ref s_luaProbeSerial);
		string token = string.Create(CultureInfo.InvariantCulture, $"ce77-live-probe-registry-{serial}");
		using (LuaFrame frame = new(state))
		{
			state.PushString("CheatEngine.SDK.CE77.LiveProbe.Registry"u8);
			state.PushString(token);
			if (!state.TryRawSet(LuaState.RegistryIndex))
			{
				return "Lua thread probe could not write its private registry marker.";
			}
		}

		lock (Gate)
		{
			if (s_luaThread.IsRunning)
			{
				return "Lua thread probe is already running; call ce77_live_probe_lua_threads_status().";
			}

			s_luaThread = LuaThreadObservation.Started(state.Handle, token);
		}

		// The delayed worker starts only after this Lua callback has returned its string to CE. It is still a live,
		// opt-in observation against CE's per-thread state contract, never a general concurrency guarantee for Lua.
		Thread thread = new(RunLuaThreadProbe)
		{
			IsBackground = true,
			Name = "CheatEngine.SDK CE77 Lua thread probe"
		};
		thread.Start();
		return
			"Lua thread/registry probe started. Do not run other Lua code for one second; poll ce77_live_probe_lua_threads_status().";
	}

	internal static string GetLuaThreadStatus()
	{
		lock (Gate)
		{
			return s_luaThread.ToDisplayString();
		}
	}

	internal static string SnapshotBeforeReset()
	{
		if (!TryRequireRuntimeAuthorization(out string denied))
		{
			return denied;
		}

		LuaState state = LuaRuntime.AcquireState();
		LuaRef reference;
		using (LuaFrame frame = new(state))
		{
			state.PushString("ce77-live-probe-reset-reference"u8);
			reference = state.CreateRef();
		}

		lock (Gate)
		{
			s_resetReference = reference;
			s_reset = ResetObservation.Before(state.Handle, reference.Epoch, reference.Reference);
		}

		return
			"Reset snapshot captured. In CE's Lua Engine call resetLuaState() manually, then call ce77_live_probe_snapshot_after_reset(). The SDK does not support an external reset that it was not told about; this only records the raw outcome.";
	}

	internal static string SnapshotAfterReset()
	{
		if (!TryRequireRuntimeAuthorization(out string denied))
		{
			return denied;
		}

		LuaRef? reference;
		lock (Gate)
		{
			reference = s_resetReference;
		}

		if (reference is null)
		{
			return "No before-reset snapshot exists. Call ce77_live_probe_snapshot_before_reset() first.";
		}

		LuaState state = LuaRuntime.AcquireState();
		bool oldReferencePushed;
		string? oldReferenceValue = null;
		using (LuaFrame frame = new(state))
		{
			oldReferencePushed = state.TryPushRef(reference);
			if (oldReferencePushed)
			{
				_ = state.TryReadString(-1, out oldReferenceValue);
			}
		}

		lock (Gate)
		{
			s_reset = s_reset.After(state.Handle, LuaRuntime.Epoch, oldReferencePushed, oldReferenceValue);
		}

		return GetStatus();
	}

	internal static string ObserveHostUserdata()
	{
		if (!TryRequireRuntimeAuthorization(out string denied))
		{
			return denied;
		}

		LuaState state = LuaRuntime.AcquireState();
		using LuaFrame frame = new(state);
		LuaStatus status = state.TryExecute("local value = getMainForm(); return type(value), tostring(value)"u8, 2,
			"=ce77_live_probe_userdata"u8);
		if (!status.IsOk)
		{
			return "getMainForm userdata observation failed with " + status + ".";
		}

		bool hasType = state.TryReadString(-2, out string? type);
		bool hasText = state.TryReadString(-1, out string? text);
		return string.Create(CultureInfo.InvariantCulture,
			$"getMainForm observation: luaType={(hasType ? type : "<not string>")}, identityText={(hasText ? text : "<not string>")}. No userdata was retained or pushed through the host pusher.");
	}

	internal static string PrepareCallbackShutdownProbe()
	{
		if (!TryRequireRuntimeAuthorization(out string denied))
		{
			return denied;
		}

		lock (Gate)
		{
			if (s_callback is { IsReleased: false })
			{
				return
					"Callback shutdown probe is already prepared. Disable this plugin, then run pcall(ce77_live_probe_callback_shutdown) from CE's Lua Engine.";
			}
		}

		LuaState state = LuaRuntime.AcquireState();
		CallbackCounter counter = new();
		LuaCallback<CallbackCounter>? callback;
		using (LuaFrame frame = new(state))
		{
			LuaStatus createStatus = LuaCallback.TryCreate(state, new LuaNativeFunction(&CallbackShutdownThunk),
				counter, out callback);
			if (!createStatus.IsOk || callback is null)
			{
				return "Callback shutdown probe could not create its callback: " + createStatus + ".";
			}

			LuaStatus registerStatus = callback.TryRegister(state, "ce77_live_probe_callback_shutdown"u8);
			if (!registerStatus.IsOk)
			{
				callback.Dispose();
				return "Callback shutdown probe could not register its callback: " + registerStatus + ".";
			}
		}

		lock (Gate)
		{
			s_callback = callback;
			s_callbackCounter = counter;
		}

		return
			"Callback prepared. First run pcall(ce77_live_probe_callback_shutdown) once (it returns a count). Then disable this plugin in CE, run pcall(ce77_live_probe_callback_shutdown) again, and preserve the raw pcall result. Re-enable and call ce77_live_probe_status().";
	}

	private static void RunSynchronizeProbe()
	{
		SynchronizeObservation observation = SynchronizeObservation.Started;
		try
		{
			SynchronizeInvocation returnThread =
				MainThread.Invoke(static _ => SynchronizeProbeState.RecordSuccessfulInvocation(), 0);
			int expectedThread = Environment.CurrentManagedThreadId;
			// The returned object stores the exact thread that ran the thunk action, including an inline nested Invoke.
			// It is carried through the result rather than read by the GUI thread after the fact.
			observation = observation with
			{
				Completion = "completed",
				WorkThreadId = returnThread.WorkThreadId,
				NestedThreadId = returnThread.NestedThreadId,
				ReturnRoundTrip = returnThread.ReturnValue,
				WorkerThreadId = expectedThread
			};

			try
			{
				MainThread.Invoke(
					static _ => throw new InvalidOperationException("CE77-live-probe expected dispatch failure."), 0);
				observation = observation with
				{
					ExceptionResult = "unexpectedly returned"
				};
			}
			catch (InvalidOperationException exception)
			{
				observation = observation with
				{
					ExceptionResult = "re-thrown: " + exception.Message
				};
			}
		}
		catch (Exception exception)
		{
			observation = observation with
			{
				Completion = "failed",
				Failure = Describe(exception)
			};
		}

		lock (Gate)
		{
			s_synchronize = observation with
			{
				IsRunning = false
			};
		}
	}

	private static void RunLuaThreadProbe()
	{
		// Give the generated Lua-function marshaller time to finish returning to CE before touching the worker's state.
		Thread.Sleep(500);
		LuaThreadObservation observation;
		lock (Gate)
		{
			observation = s_luaThread;
		}

		try
		{
			LuaState state = LuaRuntime.AcquireState();
			string? marker = null;
			LuaType markerType = LuaType.None;
			using (LuaFrame frame = new(state))
			{
				state.PushString("CheatEngine.SDK.CE77.LiveProbe.Registry"u8);
				markerType = state.RawGet(LuaState.RegistryIndex);
				_ = state.TryReadString(-1, out marker);
			}

			observation = observation with
			{
				Completion = "completed",
				WorkerState = state.Handle,
				RegistryType = markerType.ToString(),
				RegistryMarker = marker,
				RegistryMatches = string.Equals(marker, observation.ExpectedMarker, StringComparison.Ordinal)
			};
		}
		catch (Exception exception)
		{
			observation = observation with
			{
				Completion = "failed",
				Failure = Describe(exception)
			};
		}

		lock (Gate)
		{
			s_luaThread = observation with
			{
				IsRunning = false
			};
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int CallbackShutdownThunk(nint statePointer)
	{
		LuaState state = new(statePointer);
		try
		{
			if (!LuaThunk.TryGetState(state, out CallbackCounter? counter))
			{
				return LuaThunk.Fail(state, "callback shutdown probe state is unavailable"u8);
			}

			state.PushInteger(Interlocked.Increment(ref counter.Calls));
			return 1;
		}
		catch (Exception exception)
		{
			return LuaThunk.Fail(state, exception);
		}
	}

	private static bool TryRequireRuntimeAuthorization(out string denied)
	{
		return TryRequireRuntimeAuthorization(LiveProbeAuthorization.Evaluate, ProbeHostGlobals.GetOpenedProcessId,
			out denied);
	}

	// Each command performs a fresh, local check. The recorded enable-time result remains a diagnostic and cannot be
	// refreshed by a capture or command into authority for a later command.
	internal static bool TryRequireRuntimeAuthorization(Func<AuthorizationDecision> evaluateAuthorization,
		Func<long> getOpenedProcessId, out string denied)
	{
		RuntimeAuthorization runtimeAuthorization =
			EvaluateRuntimeAuthorization(evaluateAuthorization, getOpenedProcessId);
		if (runtimeAuthorization.IsAllowed)
		{
			denied = string.Empty;
			return true;
		}

		denied = "Live probe denied: " + runtimeAuthorization.Denial;
		return false;
	}

	private static bool IsRuntimeProbeAllowedUnsafe()
	{
		return s_enableAuthorization.IsAllowed && s_targetMatchesCe;
	}

	private static uint ReadUnalignedUInt32(byte* address)
	{
		return (uint) (address[0] | (address[1] << 8) | (address[2] << 16) | (address[3] << 24));
	}

	private static void WriteUnalignedUInt32(byte* address, uint value)
	{
		address[0] = (byte) value;
		address[1] = (byte) (value >> 8);
		address[2] = (byte) (value >> 16);
		address[3] = (byte) (value >> 24);
	}

	private static string Describe(Exception exception)
	{
		return exception.GetType().Name + ": " + exception.Message;
	}

	private sealed class CallbackCounter
	{
		internal int Calls;
	}

	private static class SynchronizeProbeState
	{
		internal static SynchronizeInvocation RecordSuccessfulInvocation()
		{
			int workThread = Environment.CurrentManagedThreadId;
			int nestedThread = MainThread.Invoke(static _ => Environment.CurrentManagedThreadId, 0);
			return new SynchronizeInvocation(workThread, nestedThread, "ce77-synchronize-return");
		}
	}

	private readonly record struct SynchronizeInvocation(int WorkThreadId, int NestedThreadId, string ReturnValue);

	private readonly record struct RuntimeAuthorization(
		AuthorizationDecision Authorization,
		bool TargetMatchesCe,
		string TargetMatchFailure)
	{
		internal bool IsAllowed => Authorization.IsAllowed && TargetMatchesCe;

		internal string Denial => Authorization.IsAllowed ? TargetMatchFailure : Authorization.Reason;
	}

	private readonly record struct BootstrapObservation(
		int Calls,
		nint InitRecord,
		int OpaqueArgument,
		bool Captured,
		bool TailCanaryWritten,
		int TailWriteCount,
		uint TailReadBeforeWrite,
		string? TailFailure)
	{
		internal static BootstrapObservation Empty => new(0, 0, 0, false, false, 0, 0, null);
	}

	private readonly record struct SynchronizeObservation(
		bool IsRunning,
		string Completion,
		int WorkerThreadId,
		int WorkThreadId,
		int NestedThreadId,
		string? ReturnRoundTrip,
		string? ExceptionResult,
		string? Failure)
	{
		internal static SynchronizeObservation NotStarted => new(false, "not started", 0, 0, 0, null, null, null);
		internal static SynchronizeObservation Started => new(true, "running", 0, 0, 0, null, null, null);

		internal string ToDisplayString()
		{
			return string.Create(CultureInfo.InvariantCulture,
				$"completion={Completion}, workerThread={WorkerThreadId}, thunkThread={WorkThreadId}, nestedThread={NestedThreadId}, return={ReturnRoundTrip ?? "<none>"}, exception={ExceptionResult ?? "<none>"}, failure={Failure ?? "<none>"}");
		}
	}

	private readonly record struct LuaThreadObservation(
		bool IsRunning,
		string Completion,
		nint GuiState,
		nint WorkerState,
		string ExpectedMarker,
		string? RegistryMarker,
		string RegistryType,
		bool RegistryMatches,
		string? Failure)
	{
		internal static LuaThreadObservation NotStarted =>
			new(false, "not started", 0, 0, string.Empty, null, string.Empty, false, null);

		internal static LuaThreadObservation Started(nint guiState, string marker)
		{
			return new LuaThreadObservation(true, "running", guiState, 0, marker, null, string.Empty, false, null);
		}

		internal string ToDisplayString()
		{
			return string.Create(CultureInfo.InvariantCulture,
				$"completion={Completion}, guiState=0x{GuiState:X}, workerState=0x{WorkerState:X}, registryType={RegistryType}, registryMatches={RegistryMatches}, failure={Failure ?? "<none>"}");
		}
	}

	private readonly record struct ResetObservation(
		bool HasBefore,
		nint BeforeState,
		int BeforeEpoch,
		int BeforeReference,
		bool HasAfter,
		nint AfterState,
		int AfterEpoch,
		bool OldReferencePushed,
		string? OldReferenceValue)
	{
		internal static ResetObservation NotStarted => new(false, 0, 0, 0, false, 0, 0, false, null);

		internal static ResetObservation Before(nint state, int epoch, int reference)
		{
			return new ResetObservation(true, state, epoch, reference, false, 0, 0, false, null);
		}

		internal ResetObservation After(nint state, int epoch, bool oldReferencePushed, string? oldReferenceValue)
		{
			return this with
			{
				HasAfter = true,
				AfterState = state,
				AfterEpoch = epoch,
				OldReferencePushed = oldReferencePushed,
				OldReferenceValue = oldReferenceValue
			};
		}

		internal string ToDisplayString()
		{
			if (!HasBefore)
			{
				return "not started";
			}

			return string.Create(CultureInfo.InvariantCulture,
				$"beforeState=0x{BeforeState:X}, beforeEpoch={BeforeEpoch}, beforeReference={BeforeReference}, afterCaptured={HasAfter}, afterState=0x{AfterState:X}, afterEpoch={AfterEpoch}, oldReferencePushed={OldReferencePushed}, oldReferenceValue={OldReferenceValue ?? "<none>"}");
		}
	}
}
