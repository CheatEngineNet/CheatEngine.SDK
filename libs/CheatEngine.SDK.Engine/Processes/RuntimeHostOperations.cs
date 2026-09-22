using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>Protected CE 7.7 runtime-global observations for a currently enabled plugin.</summary>
/// <remarks>
///     Each operation acquires one Lua-runtime admission, resolves its global through an attach-epoch-aware reference,
///     and restores the Lua stack on every result. The CE source catalogue does not establish a main-thread requirement
///     for these globals, so this class neither dispatches nor claims that a caller may use a state from another thread.
///     Results are copied scalar facts with no CE object, handle, or lifetime ownership.
/// </remarks>
public static class RuntimeHostOperations
{
	private static readonly LuaRef SGetCheatEngineVersion = new();
	private static readonly LuaRef SGetSystemArchitecture = new();
	private static readonly LuaRef SGetTargetAbi = new();

	/// <summary>Reads CE's legacy, coarse <c>getCEVersion</c> number.</summary>
	/// <param name="version">The finite, non-negative reported number on success; otherwise zero.</param>
	/// <returns>The protected binding result.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     This result is not a complete four-component CE file version. Consumers must not turn it into one by rounding
	///     or formatting it.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetCheatEngineVersion(out double version)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return TryGetCheatEngineVersion(operation.State, out version);
	}

	/// <summary>Reads and decodes CE's <c>getSystemArchitecture</c> host-architecture discriminant.</summary>
	/// <param name="architecture">The decoded CE host architecture, or unknown for an unrecognized documented result.</param>
	/// <returns>The protected binding result.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetSystemArchitecture(out CheatEngineArchitecture architecture)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			LuaOperationStatus status =
				TryCallInteger(state, SGetSystemArchitecture, "getSystemArchitecture"u8, out int code);
			if (!status.IsSuccess)
			{
				architecture = CheatEngineArchitecture.Unknown;
				return status;
			}

			return RuntimeInfo.TryDecodeSystemArchitecture(code, out architecture)
				? LuaOperationStatus.Success
				: LuaOperationStatus.InvalidResult;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Reads and decodes CE's <c>getABI</c> target ABI-family discriminant.</summary>
	/// <param name="abi">The decoded target ABI family, or unknown for an unrecognized documented result.</param>
	/// <returns>The protected binding result.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetTargetAbi(out TargetAbi abi)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			LuaOperationStatus status = TryCallInteger(state, SGetTargetAbi, "getABI"u8, out int code);
			if (!status.IsSuccess)
			{
				abi = TargetAbi.Unknown;
				return status;
			}

			return RuntimeInfo.TryDecodeTargetAbi(code, out abi)
				? LuaOperationStatus.Success
				: LuaOperationStatus.InvalidResult;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	internal static LuaOperationStatus TryCallInteger(LuaState state, LuaRef cache, ReadOnlySpan<byte> globalName,
		out int value)
	{
		LuaGlobalPushOutcome resolution = LuaGlobalFunctions.TryPushWithOutcome(state, cache, globalName);
		if (!resolution.IsSuccess)
		{
			value = default;
			return resolution.ToOperationStatus();
		}

		LuaStatus status = state.TryCall(0, 1);
		if (!status.IsOk)
		{
			value = default;
			return LuaOperationStatus.LuaFailure(status);
		}

		if (state.TypeOf(-1) != LuaType.Number || !state.TryReadInteger(-1, out long raw) ||
		    raw is < int.MinValue or > int.MaxValue)
		{
			value = default;
			return state.IsNil(-1) ? LuaOperationStatus.NilResult : LuaOperationStatus.InvalidResult;
		}

		value = (int) raw;
		return LuaOperationStatus.Success;
	}

	private static LuaOperationStatus TryGetCheatEngineVersion(LuaState state, out double version)
	{
		int top = state.Top;
		try
		{
			LuaGlobalPushOutcome resolution =
				LuaGlobalFunctions.TryPushWithOutcome(state, SGetCheatEngineVersion, "getCEVersion"u8);
			if (!resolution.IsSuccess)
			{
				version = default;
				return resolution.ToOperationStatus();
			}

			LuaStatus status = state.TryCall(0, 1);
			if (!status.IsOk)
			{
				version = default;
				return LuaOperationStatus.LuaFailure(status);
			}

			if (state.TypeOf(-1) != LuaType.Number || !state.TryReadNumber(-1, out version) ||
			    !double.IsFinite(version) || version < 0)
			{
				version = default;
				return state.IsNil(-1) ? LuaOperationStatus.NilResult : LuaOperationStatus.InvalidResult;
			}

			return LuaOperationStatus.Success;
		}
		finally
		{
			state.SetTop(top);
		}
	}
}
