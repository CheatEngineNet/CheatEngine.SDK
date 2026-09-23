using System;
using System.Diagnostics;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>
///     The single implementation of Cheat Engine's "selected PID, target facts, selected PID" read sequence, shared by
///     the instruction profile, the runtime process operations and the runtime snapshot.
/// </summary>
/// <remarks>
///     <para>
///         Every call runs inside the caller's <c>LuaRuntimeOperation</c> and restores the recorded stack top on every
///         exit path. Each global is resolved through <c>LuaGlobalFunctions.TryPushWithOutcome</c> and a static
///         <see cref="LuaRef" /> cache, so an absent global, a raising global (with its protected <see cref="LuaStatus" />)
///         and a value of the wrong type stay three distinct outcomes. No error text is read.
///     </para>
///     <para>
///         The process identifier is read first. With no target selected, CE 7.7.0.10621 reports exactly the facts of an
///         x64 target (<c>targetIsX86</c>, <c>targetIs64Bit</c> and an 8-byte pointer size; spike C3 D2, ObservedHost,
///         Lua-only, 2026-09-22), so a zero identifier stops the probe before any fact is read. The file-as-process
///         sentinel 4294967295 also stops it: <c>openFileAsProcess</c> stores <c>processid:=$FFFFFFFF</c> and
///         <c>getOpenedProcessID</c> pushes that <c>dword</c> as a Lua integer (<c>LuaHandler.pas:14101-14108</c>,
///         <c>:4228-4232</c> at cheat-engine/cheat-engine@ec45d5f, ObservedSource; not observed on the 7.7 binary). That
///         value is not a multiple of four, so it can never be a Windows process identifier.
///     </para>
///     <para>
///         The probe only reads. It never calls <c>setPointerSize</c>, <c>setAssemblerMode</c>, <c>openProcess</c>,
///         <c>openFileAsProcess</c> or any driver global (audit A17-18, Q45). The second identifier read makes a
///         selection change during the probe observable; it is not a lock and does not detect an unobserved A-to-B-to-A
///         transition.
///     </para>
/// </remarks>
internal static class TargetArchitectureProbe
{
	/// <summary>The value <c>getOpenedProcessID</c> returns for a file opened as a process: <c>$FFFFFFFF</c> as a Lua integer.</summary>
	internal const long FileAsProcessSentinel = 4294967295L;

	private static readonly LuaRef SGetOpenedProcessId = new();
	private static readonly LuaRef SIsConnectedToCeServer = new();
	private static readonly LuaRef STargetIs64Bit = new();
	private static readonly LuaRef STargetIsX86 = new();
	private static readonly LuaRef STargetIsArm = new();
	private static readonly LuaRef STargetIsAndroid = new();
	private static readonly LuaRef SGetAbi = new();
	private static readonly LuaRef SGetPointerSize = new();

	/// <summary>
	///     Reads the selected PID, the requested facts in their fixed order, and the selected PID again, under the
	///     caller's Lua operation.
	/// </summary>
	/// <param name="state">The admitted Lua state of the caller's operation.</param>
	/// <param name="read">The facts to read; <see cref="TargetProbeFacts.SelectedProcess" /> is always read.</param>
	/// <param name="required">
	///     The subset of <paramref name="read" /> whose absence fails the probe with
	///     <see cref="TargetProbeStatus.GlobalUnavailable" />; any other absent fact stays <see langword="null" />.
	/// </param>
	/// <returns>The status and every fact read up to the point the probe stopped.</returns>
	internal static TargetProbeResult Observe(LuaState state, TargetProbeFacts read, TargetProbeFacts required)
	{
		Debug.Assert((read & TargetProbeFacts.SelectedProcess) == TargetProbeFacts.None,
			"The selected process is always read.");
		Debug.Assert((required & ~read) == TargetProbeFacts.None, "A required fact must also be requested.");

		int top = state.Top;
		Accumulator facts = default;
		try
		{
			TargetProbeStatus status = ReadProcessId(state, out int processId, out LuaStatus luaStatus);
			facts.RecordProcessIdRead(status);
			if (status != TargetProbeStatus.Success)
			{
				return facts.ToResult(status, luaStatus, 0);
			}

			status = ReadFacts(state, read, required, ref facts, out luaStatus);
			if (status != TargetProbeStatus.Success)
			{
				return facts.ToResult(status, luaStatus, processId);
			}

			// The closing read keeps its own failure (never a partial success) and turns any other selection, no
			// selection or the file-as-process sentinel into a target change.
			status = ReadProcessId(state, out int finalProcessId, out luaStatus);
			status = status switch
			{
				TargetProbeStatus.Success when finalProcessId == processId => TargetProbeStatus.Success,
				TargetProbeStatus.Success or TargetProbeStatus.NoTargetSelected or TargetProbeStatus.FileAsProcess =>
					TargetProbeStatus.TargetChanged,
				_ => status
			};
			return facts.ToResult(status, luaStatus, processId);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Reads and classifies <c>getOpenedProcessID</c> once, leaving the stack as it found it.</summary>
	/// <param name="state">The admitted Lua state of the caller's operation.</param>
	/// <param name="processId">The positive identifier when the result is <see cref="TargetProbeStatus.Success" />; otherwise zero.</param>
	/// <param name="luaStatus">The protected status of a <see cref="TargetProbeStatus.LuaFailure" />; otherwise OK.</param>
	/// <returns>
	///     <see cref="TargetProbeStatus.Success" />, <see cref="TargetProbeStatus.NoTargetSelected" />,
	///     <see cref="TargetProbeStatus.FileAsProcess" />, <see cref="TargetProbeStatus.InvalidProcessId" />,
	///     <see cref="TargetProbeStatus.GlobalUnavailable" /> or <see cref="TargetProbeStatus.LuaFailure" />.
	/// </returns>
	internal static TargetProbeStatus ReadProcessId(LuaState state, out int processId, out LuaStatus luaStatus)
	{
		processId = 0;
		luaStatus = LuaStatus.Ok;
		int top = state.Top;
		try
		{
			LuaGlobalPushOutcome global =
				LuaGlobalFunctions.TryPushWithOutcome(state, SGetOpenedProcessId, "getOpenedProcessID"u8);
			if (!global.IsSuccess)
			{
				return FromResolution(global, out luaStatus);
			}

			LuaStatus call = state.TryCall(0, 1);
			if (!call.IsOk)
			{
				luaStatus = call;
				return TargetProbeStatus.LuaFailure;
			}

			// getOpenedProcessID pushes a Lua integer; a float, even an integral one, is malformed like every other
			// integer fact the probe reads.
			if (!state.IsInteger(-1) || !state.TryReadInteger(-1, out long raw))
			{
				return TargetProbeStatus.InvalidProcessId;
			}

			return ClassifyProcessId(raw, out processId);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Classifies a raw <c>getOpenedProcessID</c> integer.</summary>
	/// <param name="raw">The Lua integer.</param>
	/// <param name="processId">The positive identifier when the result is <see cref="TargetProbeStatus.Success" />; otherwise zero.</param>
	/// <returns>The selected, no-target, file-as-process or invalid category.</returns>
	internal static TargetProbeStatus ClassifyProcessId(long raw, out int processId)
	{
		processId = 0;
		if (raw == 0)
		{
			return TargetProbeStatus.NoTargetSelected;
		}

		// Compare as a 64-bit value before the int range check: the sentinel is an unsigned dword, not -1.
		if (raw == FileAsProcessSentinel)
		{
			return TargetProbeStatus.FileAsProcess;
		}

		if (raw is < 0 or > int.MaxValue)
		{
			return TargetProbeStatus.InvalidProcessId;
		}

		processId = (int) raw;
		return TargetProbeStatus.Success;
	}

	private static TargetProbeStatus ReadFacts(LuaState state, TargetProbeFacts read, TargetProbeFacts required,
		ref Accumulator facts, out LuaStatus luaStatus)
	{
		TargetProbeStatus status = ReadFact(state, read, required, TargetProbeFacts.CeServerConnection,
			SIsConnectedToCeServer, "isConnectedToCEServer"u8, ref facts, out luaStatus);
		if (status != TargetProbeStatus.Success)
		{
			return status;
		}

		status = ReadFact(state, read, required, TargetProbeFacts.Bitness, STargetIs64Bit, "targetIs64Bit"u8,
			ref facts, out luaStatus);
		if (status != TargetProbeStatus.Success)
		{
			return status;
		}

		status = ReadFact(state, read, required, TargetProbeFacts.X86Family, STargetIsX86, "targetIsX86"u8, ref facts,
			out luaStatus);
		if (status != TargetProbeStatus.Success)
		{
			return status;
		}

		status = ReadFact(state, read, required, TargetProbeFacts.ArmFamily, STargetIsArm, "targetIsArm"u8, ref facts,
			out luaStatus);
		if (status != TargetProbeStatus.Success)
		{
			return status;
		}

		status = ReadFact(state, read, required, TargetProbeFacts.Android, STargetIsAndroid, "targetIsAndroid"u8,
			ref facts, out luaStatus);
		if (status != TargetProbeStatus.Success)
		{
			return status;
		}

		status = ReadFact(state, read, required, TargetProbeFacts.Abi, SGetAbi, "getABI"u8, ref facts, out luaStatus);
		return status != TargetProbeStatus.Success
			? status
			: ReadFact(state, read, required, TargetProbeFacts.ConfiguredPointerSize, SGetPointerSize,
				"getPointerSize"u8, ref facts, out luaStatus);
	}

	private static TargetProbeStatus ReadFact(LuaState state, TargetProbeFacts read, TargetProbeFacts required,
		TargetProbeFacts fact, LuaRef cache, ReadOnlySpan<byte> name, ref Accumulator facts, out LuaStatus luaStatus)
	{
		luaStatus = LuaStatus.Ok;
		if ((read & fact) == TargetProbeFacts.None)
		{
			return TargetProbeStatus.Success;
		}

		int top = state.Top;
		try
		{
			LuaGlobalPushOutcome global = LuaGlobalFunctions.TryPushWithOutcome(state, cache, name);
			if (global.Status == LuaGlobalPushStatus.Unavailable)
			{
				facts.Absent |= fact;
				return (required & fact) != TargetProbeFacts.None
					? TargetProbeStatus.GlobalUnavailable
					: TargetProbeStatus.Success;
			}

			if (!global.IsSuccess)
			{
				return FromResolution(global, out luaStatus);
			}

			LuaStatus call = state.TryCall(0, 1);
			if (!call.IsOk)
			{
				luaStatus = call;
				return TargetProbeStatus.LuaFailure;
			}

			if (IsIntegerFact(fact))
			{
				// CE pushes these with lua_pushinteger. A float, even an integral one, and a value outside int are
				// refused rather than rounded or truncated.
				if (!state.IsInteger(-1) || !state.TryReadInteger(-1, out long raw) || raw is < int.MinValue or > int.MaxValue)
				{
					return TargetProbeStatus.InvalidResult;
				}

				facts.Store(fact, (int) raw);
			}
			else
			{
				// nil is not false: a boolean fact must be a Lua boolean.
				if (state.TypeOf(-1) != LuaType.Boolean)
				{
					return TargetProbeStatus.InvalidResult;
				}

				facts.Store(fact, state.ToBoolean(-1));
			}

			return TargetProbeStatus.Success;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static bool IsIntegerFact(TargetProbeFacts fact)
	{
		return fact is TargetProbeFacts.Abi or TargetProbeFacts.ConfiguredPointerSize;
	}

	private static TargetProbeStatus FromResolution(LuaGlobalPushOutcome global, out LuaStatus luaStatus)
	{
		if (global.Status == LuaGlobalPushStatus.Unavailable)
		{
			luaStatus = LuaStatus.Ok;
			return TargetProbeStatus.GlobalUnavailable;
		}

		luaStatus = global.LuaStatus;
		return TargetProbeStatus.LuaFailure;
	}

	private struct Accumulator
	{
		internal TargetProbeFacts Resolved;
		internal TargetProbeFacts Absent;
		private bool? _isConnectedToCeServer;
		private bool? _is64Bit;
		private bool? _isX86Family;
		private bool? _isArmFamily;
		private bool? _isAndroid;
		private int? _abiCode;
		private int? _configuredPointerSizeBytes;

		internal void RecordProcessIdRead(TargetProbeStatus status)
		{
			if (status == TargetProbeStatus.GlobalUnavailable)
			{
				Absent |= TargetProbeFacts.SelectedProcess;
			}
			else if (status is TargetProbeStatus.Success or TargetProbeStatus.NoTargetSelected
					 or TargetProbeStatus.FileAsProcess)
			{
				Resolved |= TargetProbeFacts.SelectedProcess;
			}
		}

		internal void Store(TargetProbeFacts fact, bool value)
		{
			Resolved |= fact;
			switch (fact)
			{
				case TargetProbeFacts.CeServerConnection:
					_isConnectedToCeServer = value;
					break;
				case TargetProbeFacts.Bitness:
					_is64Bit = value;
					break;
				case TargetProbeFacts.X86Family:
					_isX86Family = value;
					break;
				case TargetProbeFacts.ArmFamily:
					_isArmFamily = value;
					break;
				case TargetProbeFacts.Android:
					_isAndroid = value;
					break;
				default:
					Debug.Fail("Not a boolean fact.");
					break;
			}
		}

		internal void Store(TargetProbeFacts fact, int value)
		{
			Resolved |= fact;
			if (fact == TargetProbeFacts.Abi)
			{
				_abiCode = value;
			}
			else
			{
				Debug.Assert(fact == TargetProbeFacts.ConfiguredPointerSize, "Not an integer fact.");
				_configuredPointerSizeBytes = value;
			}
		}

		internal readonly TargetProbeResult ToResult(TargetProbeStatus status, LuaStatus luaStatus, int processId)
		{
			return new TargetProbeResult(status, luaStatus, processId, Resolved, Absent, _isConnectedToCeServer,
				_is64Bit, _isX86Family, _isArmFamily, _isAndroid, _abiCode, _configuredPointerSizeBytes);
		}
	}
}
