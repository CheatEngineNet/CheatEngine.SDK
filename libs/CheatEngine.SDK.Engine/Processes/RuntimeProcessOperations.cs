using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>Protected CE 7.7 operations for observing and explicitly selecting the current target process.</summary>
/// <remarks>
///     <para>
///         The operations use <c>getOpenedProcessID</c>, <c>openProcess</c> (only in
///         <see cref="SelectAndObserve" />), <c>targetIs64Bit</c> and, for <see cref="ObserveTargetArchitecture" /> and
///         <see cref="TryGetConfiguredPointerSize" />, the read-only target probes <c>isConnectedToCEServer</c>,
///         <c>targetIsX86</c>, <c>targetIsArm</c>, <c>targetIsAndroid</c>, <c>getABI</c> and <c>getPointerSize</c>. They
///         preserve absence, unavailable-global, protected-Lua, malformed-result, target-change, file-as-process and
///         unconfirmed-selection outcomes without parsing Lua error text. They never call <c>setPointerSize</c>,
///         <c>setAssemblerMode</c>, <c>openFileAsProcess</c> or a driver global (audit A17-18, Q45).
///     </para>
///     <para>
///         They do not provide name lookup, process enumeration, automatic selection, OS-handle ownership,
///         process-lifetime atomicity, or a main-thread dispatch guarantee. CE's source catalogue does not prove a
///         GUI-thread requirement for this subset, so calls run on the acquiring thread's host-provided Lua state under
///         one lifecycle admission. With no target selected Cheat Engine reports x64-like facts (spike C3 D2), so every
///         observation reads <c>getOpenedProcessID</c> first and reads no target fact when it is 0.
///     </para>
/// </remarks>
public static class RuntimeProcessOperations
{
	private static readonly LuaRef SOpenProcess = new();
	private static readonly LuaRef STargetIs64Bit = new();

	/// <summary>Observes the current CE target process and its bitness.</summary>
	/// <param name="observation">The copied target observation only when the returned status is successful.</param>
	/// <returns>
	///     The factual protected process-observation status: <see cref="ProcessOperationStatusKind.TargetNotAttached" />
	///     for identifier 0, <see cref="ProcessOperationStatusKind.FileAsProcessTarget" /> for the file-as-process
	///     sentinel 4294967295, and <see cref="ProcessOperationStatusKind.InvalidResult" /> for any other identifier
	///     outside (0, <see cref="int.MaxValue" />].
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return ObserveCurrent(operation.State, out observation);
	}

	/// <summary>Selects an explicit process identifier and immediately verifies CE's resulting selection.</summary>
	/// <param name="processId">The positive process identifier to select.</param>
	/// <param name="observation">The copied matching target observation only when the returned status is successful.</param>
	/// <returns>
	///     The factual protected selection status. A normal <c>openProcess</c> return is not success by itself: success
	///     requires the next <c>getOpenedProcessID</c> observation to equal <paramref name="processId" />.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="processId" /> is default or otherwise non-positive.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     This is the only operation of this class that changes Cheat Engine's state. Selecting a process also resets
	///     Cheat Engine's configured pointer size to the target default (spike C3 D3c).
	/// </remarks>
	[RequiresPluginEnabled]
	public static ProcessOperationStatus SelectAndObserve(TargetProcessId processId,
		out CurrentProcessObservation observation)
	{
		ValidateProcessId(processId);
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			ProcessOperationStatus status = TryOpenProcess(state, processId);
			if (!status.IsSuccess)
			{
				observation = default;
				return status;
			}

			status = TryGetOpenedProcessId(state, out TargetProcessId? observedProcessId);
			if (status.Kind == ProcessOperationStatusKind.TargetNotAttached ||
			    (status.IsSuccess && observedProcessId != processId))
			{
				observation = default;
				return ProcessOperationStatus.SelectionNotConfirmed;
			}

			if (!status.IsSuccess)
			{
				observation = default;
				return status;
			}

			status = TryGetTargetBitness(state, out PointerSize bitness);
			if (!status.IsSuccess)
			{
				observation = default;
				return status;
			}

			observation = new CurrentProcessObservation(observedProcessId!.Value, bitness);
			return ProcessOperationStatus.Success;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>
	///     Reads Cheat Engine's configured pointer size (<c>getPointerSize</c>) for the selected target, bracketed by
	///     two selected-PID reads in one Lua admission.
	/// </summary>
	/// <param name="rawBytes">
	///     The raw integer Cheat Engine returned when it is a Lua integer that fits <see cref="int" />, also for an
	///     <see cref="ProcessOperationStatusKind.InvalidResult" /> width; otherwise zero.
	/// </param>
	/// <param name="pointerSize">The configured size when it is exactly 4 or 8 bytes; otherwise unknown.</param>
	/// <returns>
	///     <see cref="ProcessOperationStatusKind.Success" /> for a raw 4 or 8;
	///     <see cref="ProcessOperationStatusKind.InvalidResult" /> for any other integer (kept in
	///     <paramref name="rawBytes" />), a float or a value outside <see cref="int" />;
	///     <see cref="ProcessOperationStatusKind.TargetNotAttached" /> without reading the value when no target is
	///     selected; <see cref="ProcessOperationStatusKind.FileAsProcessTarget" />,
	///     <see cref="ProcessOperationStatusKind.TargetChanged" />,
	///     <see cref="ProcessOperationStatusKind.GlobalUnavailable" /> or
	///     <see cref="ProcessOperationStatusKind.ProtectedLuaFailure" /> otherwise.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     <para>
	///         The configured pointer size is not the target bitness, and neither comes from the plugin's
	///         <c>IntPtr.Size</c>. On CE 7.7.0.10621 x64, <c>getPointerSize</c> reported 4 on a 64-bit target after
	///         <c>setPointerSize(4)</c> while <c>targetIs64Bit</c> stayed true and <c>readPointer</c> still read 8 bytes;
	///         <c>setPointerSize</c> accepted 2; re-selecting the target reset the value (spike C3 D3, Lua-only,
	///         ObservedHost design input). The value is per-attachment state: any later selection invalidates it.
	///     </para>
	///     <para>The SDK never calls <c>setPointerSize</c>.</para>
	/// </remarks>
	[RequiresPluginEnabled]
	public static ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize)
	{
		rawBytes = 0;
		pointerSize = PointerSize.Unknown;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		TargetProbeResult probe = TargetArchitectureProbe.Observe(operation.State,
			TargetProbeFacts.ConfiguredPointerSize, TargetProbeFacts.ConfiguredPointerSize);
		if (probe.Status != TargetProbeStatus.Success)
		{
			return FromProbe(probe);
		}

		rawBytes = probe.ConfiguredPointerSizeBytes.GetValueOrDefault();
		switch (rawBytes)
		{
			case 4:
				pointerSize = PointerSize.Bit32;
				return ProcessOperationStatus.Success;
			case 8:
				pointerSize = PointerSize.Bit64;
				return ProcessOperationStatus.Success;
			default:
				return ProcessOperationStatus.InvalidResult;
		}
	}

	/// <summary>
	///     Observes Cheat Engine's separate facts about the selected target (backend, bitness, ISA families, Android,
	///     ABI and configured pointer size) between two selected-PID reads in one Lua admission.
	/// </summary>
	/// <param name="observation">The copied facts only when the returned status is successful.</param>
	/// <returns>
	///     <see cref="ProcessOperationStatusKind.Success" /> when the PID was positive and unchanged and
	///     <c>targetIs64Bit</c> returned a boolean; <see cref="ProcessOperationStatusKind.TargetNotAttached" /> and
	///     <see cref="ProcessOperationStatusKind.FileAsProcessTarget" /> without reading any fact;
	///     <see cref="ProcessOperationStatusKind.TargetChanged" /> when the bracketing reads differ;
	///     <see cref="ProcessOperationStatusKind.GlobalUnavailable" /> when <c>getOpenedProcessID</c> or
	///     <c>targetIs64Bit</c> is absent; <see cref="ProcessOperationStatusKind.ProtectedLuaFailure" /> when any probe
	///     raises; <see cref="ProcessOperationStatusKind.InvalidResult" /> when any probe returns a value of the wrong
	///     type (a <c>nil</c> boolean is malformed, not <see langword="false" />).
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     <para>
	///         The optional globals (<c>isConnectedToCEServer</c>, <c>targetIsX86</c>, <c>targetIsArm</c>,
	///         <c>targetIsAndroid</c>, <c>getABI</c>, <c>getPointerSize</c>) may be absent: their facts are then
	///         <see langword="null" /> or unknown in <paramref name="observation" />, never <see langword="false" />. A
	///         CEServer connection is reported as <see cref="TargetBackend.CEServer" /> together with the facts CE reports
	///         about the remote target; only <c>Targets.TargetSelection</c> refuses such a target for incarnation
	///         evidence.
	///     </para>
	///     <para>
	///         Evidence: spike C3 D2/D3/D5 (CE 7.7.0.10621 x64, Lua-only, ObservedHost design input) and
	///         <c>LuaHandler.pas:8280-8313</c>, <c>:10906-10918</c>, <c>:15962-15966</c> at ec45d5f (ObservedSource).
	///         Fixture tests cover this operation at C1/C2; it is not host-qualified.
	///     </para>
	/// </remarks>
	[RequiresPluginEnabled]
	public static ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return ObserveTargetArchitectureCore(operation.State, out observation, out _);
	}

	internal static ProcessOperationStatus ObserveTargetArchitectureCore(LuaState state,
		out TargetArchitectureObservation observation, out TargetProbeResult probe)
	{
		probe = TargetArchitectureProbe.Observe(state, TargetProbeFacts.AllTargetFacts, TargetProbeFacts.Bitness);
		if (probe.Status != TargetProbeStatus.Success)
		{
			observation = default;
			return FromProbe(probe);
		}

		TargetBackend backend = probe.IsConnectedToCeServer switch
		{
			true => TargetBackend.CEServer,
			false => TargetBackend.LocalProcess,
			null => TargetBackend.Unknown
		};
		observation = new TargetArchitectureObservation(new TargetProcessId(probe.ProcessId), backend,
			probe.Is64Bit.GetValueOrDefault() ? PointerSize.Bit64 : PointerSize.Bit32, probe.IsX86Family,
			probe.IsArmFamily, probe.IsAndroid, probe.AbiCode, probe.ConfiguredPointerSizeBytes);
		return ProcessOperationStatus.Success;
	}

	internal static ProcessOperationStatus FromProbe(in TargetProbeResult probe)
	{
		return FromProbe(probe.Status, probe.LuaStatus);
	}

	private static ProcessOperationStatus FromProbe(TargetProbeStatus status, LuaStatus luaStatus)
	{
		return status switch
		{
			TargetProbeStatus.Success => ProcessOperationStatus.Success,
			TargetProbeStatus.NoTargetSelected => ProcessOperationStatus.TargetNotAttached,
			TargetProbeStatus.FileAsProcess => ProcessOperationStatus.FileAsProcessTarget,
			TargetProbeStatus.TargetChanged => ProcessOperationStatus.TargetChanged,
			TargetProbeStatus.GlobalUnavailable => ProcessOperationStatus.GlobalUnavailable,
			TargetProbeStatus.LuaFailure => ProcessOperationStatus.ProtectedLuaFailure(luaStatus),
			_ => ProcessOperationStatus.InvalidResult
		};
	}

	private static ProcessOperationStatus ObserveCurrent(LuaState state, out CurrentProcessObservation observation)
	{
		int top = state.Top;
		try
		{
			ProcessOperationStatus status = TryGetOpenedProcessId(state, out TargetProcessId? processId);
			if (!status.IsSuccess || !processId.HasValue)
			{
				observation = default;
				return status;
			}

			status = TryGetTargetBitness(state, out PointerSize bitness);
			if (!status.IsSuccess)
			{
				observation = default;
				return status;
			}

			observation = new CurrentProcessObservation(processId.Value, bitness);
			return ProcessOperationStatus.Success;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static ProcessOperationStatus TryGetOpenedProcessId(LuaState state, out TargetProcessId? processId)
	{
		TargetProbeStatus status = TargetArchitectureProbe.ReadProcessId(state, out int value, out LuaStatus luaStatus);
		processId = status == TargetProbeStatus.Success ? new TargetProcessId(value) : null;
		return FromProbe(status, luaStatus);
	}

	private static ProcessOperationStatus TryGetTargetBitness(LuaState state, out PointerSize bitness)
	{
		ProcessOperationStatus status = TryCallBoolean(state, STargetIs64Bit, "targetIs64Bit"u8, out bool is64Bit);
		if (!status.IsSuccess)
		{
			bitness = PointerSize.Unknown;
			return status;
		}

		bitness = is64Bit ? PointerSize.Bit64 : PointerSize.Bit32;
		return ProcessOperationStatus.Success;
	}

	private static ProcessOperationStatus TryOpenProcess(LuaState state, TargetProcessId processId)
	{
		LuaGlobalPushOutcome resolution = LuaGlobalFunctions.TryPushWithOutcome(state, SOpenProcess, "openProcess"u8);
		if (!resolution.IsSuccess)
		{
			return FromResolution(resolution);
		}

		state.PushInteger(processId.Value);
		LuaStatus luaStatus = state.TryCall(1, 0);
		return luaStatus.IsOk
			? ProcessOperationStatus.Success
			: ProcessOperationStatus.ProtectedLuaFailure(luaStatus);
	}

	private static ProcessOperationStatus TryCallBoolean(LuaState state, LuaRef cache, ReadOnlySpan<byte> globalName,
		out bool value)
	{
		LuaGlobalPushOutcome resolution = LuaGlobalFunctions.TryPushWithOutcome(state, cache, globalName);
		if (!resolution.IsSuccess)
		{
			value = default;
			return FromResolution(resolution);
		}

		LuaStatus luaStatus = state.TryCall(0, 1);
		if (!luaStatus.IsOk)
		{
			value = default;
			return ProcessOperationStatus.ProtectedLuaFailure(luaStatus);
		}

		if (state.TypeOf(-1) != LuaType.Boolean)
		{
			value = default;
			return ProcessOperationStatus.InvalidResult;
		}

		value = state.ToBoolean(-1);
		return ProcessOperationStatus.Success;
	}

	private static ProcessOperationStatus FromResolution(LuaGlobalPushOutcome resolution)
	{
		return resolution.Status switch
		{
			LuaGlobalPushStatus.Unavailable => ProcessOperationStatus.GlobalUnavailable,
			LuaGlobalPushStatus.LuaFailure => ProcessOperationStatus.ProtectedLuaFailure(resolution.LuaStatus),
			_ => ProcessOperationStatus.InvalidResult
		};
	}

	private static void ValidateProcessId(TargetProcessId processId)
	{
		if (processId.Value <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(processId), processId.Value,
				"A target process identifier must be positive.");
		}
	}
}
