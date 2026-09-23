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
///     <para>
///         Each operation acquires one Lua-runtime admission, resolves its global through an attach-epoch-aware
///         reference, and restores the Lua stack on every result. The CE source catalogue does not establish a
///         main-thread requirement for these globals, so this class neither dispatches nor claims that a caller may use
///         a state from another thread. Results are copied scalar facts with no CE object, handle, or lifetime
///         ownership.
///     </para>
///     <para>
///         Every global read here is a read-only query of the Cheat Engine host: <c>getCEVersion</c>,
///         <c>getCheatEngineFileVersion</c>, <c>getSystemArchitecture</c>, <c>cheatEngineIs64Bit</c>,
///         <c>getOperatingSystem</c> and <c>getABI</c>. No operation loads a driver, changes a setting or selects a
///         target (audit A17-18, Q45). An absent global, a raising global, a raw <c>nil</c> and a malformed value keep
///         distinct <see cref="LuaOperationStatus" /> kinds, and no outcome is parsed from Lua error text.
///     </para>
/// </remarks>
public static class RuntimeHostOperations
{
	private static readonly LuaRef SGetCheatEngineVersion = new();
	private static readonly LuaRef SGetCheatEngineFileVersion = new();
	private static readonly LuaRef SGetSystemArchitecture = new();
	private static readonly LuaRef SCheatEngineIs64Bit = new();
	private static readonly LuaRef SGetOperatingSystem = new();
	private static readonly LuaRef SGetTargetAbi = new();

	/// <summary>Reads CE's legacy, coarse <c>getCEVersion</c> number.</summary>
	/// <param name="version">The finite, non-negative reported number on success; otherwise zero.</param>
	/// <returns>The protected binding result.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     This result is not a complete four-component CE file version. Consumers must not turn it into one by rounding
	///     or formatting it; <see cref="TryGetCheatEngineFileVersion" /> reads the complete version.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetCheatEngineVersion(out double version)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return TryGetCheatEngineVersion(operation.State, out version);
	}

	/// <summary>Reads CE's complete file version through <c>getCheatEngineFileVersion</c>.</summary>
	/// <param name="version">The four version components on success; otherwise the default value.</param>
	/// <returns>
	///     The protected binding result: <see cref="LuaOperationStatusKind.NilResult" /> when CE returned no value (the
	///     public source returns nothing when the executable's version resource is unreadable) and
	///     <see cref="LuaOperationStatusKind.InvalidResult" /> when the packed integer is not a non-negative Lua integer,
	///     the second value is neither a table nor <c>nil</c>, or the table's <c>major</c>, <c>minor</c>,
	///     <c>release</c> or <c>build</c> field is not the integer the packed value encodes.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     CE 7.7.0.10621 x64 returned two values: the integer <c>0x700070000297D</c> and a table with <c>major</c>,
	///     <c>minor</c>, <c>release</c>, <c>build</c> and string fields (spike C3 D5, Lua-only, ObservedHost design
	///     input; <c>LuaHandler.pas:13271-13324</c> at ec45d5f, ObservedSource). The integer is authoritative and
	///     decoded by <see cref="RuntimeInfo.TryDecodeFileVersion" />; the table is a cross-check, and its extra fields
	///     (<c>FileVersion</c>, <c>ProductVersion</c>, ...) are ignored.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetCheatEngineFileVersion(out CheatEngineVersion version)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return TryReadFileVersion(operation.State, out version);
	}

	/// <summary>Reads and decodes CE's <c>getSystemArchitecture</c> host-architecture discriminant.</summary>
	/// <param name="architecture">The decoded CE host architecture, or unknown for an unrecognized documented result.</param>
	/// <returns>The protected binding result.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetSystemArchitecture(out CheatEngineArchitecture architecture)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return TryReadSystemArchitecture(operation.State, out architecture);
	}

	/// <summary>Reads CE's <c>cheatEngineIs64Bit</c> flag: whether the Cheat Engine host process is 64-bit.</summary>
	/// <param name="is64Bit">The reported flag on success; otherwise <see langword="false" />.</param>
	/// <returns>
	///     The protected binding result: <see cref="LuaOperationStatusKind.NilResult" /> for <c>nil</c> and
	///     <see cref="LuaOperationStatusKind.InvalidResult" /> for any other non-boolean value.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>This host fact is never derived from <c>getSystemArchitecture</c> or from the plugin's own width.</remarks>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryIsCheatEngine64Bit(out bool is64Bit)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return TryCallBoolean(operation.State, SCheatEngineIs64Bit, "cheatEngineIs64Bit"u8, out is64Bit);
	}

	/// <summary>Reads and decodes CE's <c>getOperatingSystem</c> discriminant.</summary>
	/// <param name="operatingSystem">The decoded operating system, or unknown when the call did not succeed.</param>
	/// <returns>
	///     The protected binding result; a code outside <see cref="RuntimeInfo.TryDecodeOperatingSystem" /> is
	///     <see cref="LuaOperationStatusKind.InvalidResult" />.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetOperatingSystem(out CheatEngineOperatingSystem operatingSystem)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return TryReadOperatingSystem(operation.State, out operatingSystem);
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

	/// <summary>
	///     Reads the four Cheat Engine host facts (file version, system architecture, 64-bit flag, operating system) in
	///     one Lua admission.
	/// </summary>
	/// <param name="host">The copied host facts on success; otherwise the default value.</param>
	/// <returns>
	///     Success when every present global returned a well-formed value. An absent global is not a failure: its field
	///     stays <see langword="null" /> or unknown. A raising global is <see cref="LuaOperationStatusKind.LuaFailure" />,
	///     a <c>nil</c> where a value is required is <see cref="LuaOperationStatusKind.NilResult" />, and a malformed
	///     value is <see cref="LuaOperationStatusKind.InvalidResult" />; none of them is downgraded to "unknown".
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     <c>getCheatEngineFileVersion</c> may legitimately return no value; <see cref="CheatEngineHostObservation.FileVersion" />
	///     is then <see langword="null" />, as for an absent global. Nothing is inferred between the fields.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaOperationStatus ObserveHost(out CheatEngineHostObservation host)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		return ObserveHost(operation.State, out host, out _);
	}

	internal static LuaOperationStatus ObserveHost(LuaState state, out CheatEngineHostObservation host,
		out HostProbeFacts absent)
	{
		host = default;
		absent = HostProbeFacts.None;

		LuaOperationStatus status = TryReadFileVersion(state, out CheatEngineVersion fileVersion);
		CheatEngineVersion? observedFileVersion = fileVersion;
		if (status.Kind is LuaOperationStatusKind.GlobalUnavailable or LuaOperationStatusKind.NilResult)
		{
			absent |= status.Kind == LuaOperationStatusKind.GlobalUnavailable
				? HostProbeFacts.FileVersion
				: HostProbeFacts.None;
			observedFileVersion = null;
		}
		else if (!status.IsSuccess)
		{
			return status;
		}

		status = TryReadSystemArchitecture(state, out CheatEngineArchitecture systemArchitecture);
		if (!Accept(status, HostProbeFacts.SystemArchitecture, ref absent))
		{
			return status;
		}

		status = TryCallBoolean(state, SCheatEngineIs64Bit, "cheatEngineIs64Bit"u8, out bool is64Bit);
		bool? cheatEngineIs64Bit = status.IsSuccess ? is64Bit : null;
		if (!Accept(status, HostProbeFacts.CheatEngineBitness, ref absent))
		{
			return status;
		}

		status = TryReadOperatingSystem(state, out CheatEngineOperatingSystem operatingSystem);
		if (!Accept(status, HostProbeFacts.OperatingSystem, ref absent))
		{
			return status;
		}

		host = new CheatEngineHostObservation(observedFileVersion, systemArchitecture, cheatEngineIs64Bit,
			operatingSystem);
		return LuaOperationStatus.Success;
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

	private static bool Accept(LuaOperationStatus status, HostProbeFacts fact, ref HostProbeFacts absent)
	{
		if (status.Kind == LuaOperationStatusKind.GlobalUnavailable)
		{
			absent |= fact;
			return true;
		}

		return status.IsSuccess;
	}

	private static LuaOperationStatus TryReadSystemArchitecture(LuaState state,
		out CheatEngineArchitecture architecture)
	{
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

	private static LuaOperationStatus TryReadOperatingSystem(LuaState state,
		out CheatEngineOperatingSystem operatingSystem)
	{
		int top = state.Top;
		try
		{
			LuaOperationStatus status =
				TryCallInteger(state, SGetOperatingSystem, "getOperatingSystem"u8, out int code);
			if (!status.IsSuccess)
			{
				operatingSystem = CheatEngineOperatingSystem.Unknown;
				return status;
			}

			return RuntimeInfo.TryDecodeOperatingSystem(code, out operatingSystem)
				? LuaOperationStatus.Success
				: LuaOperationStatus.InvalidResult;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static LuaOperationStatus TryCallBoolean(LuaState state, LuaRef cache, ReadOnlySpan<byte> globalName,
		out bool value)
	{
		value = false;
		int top = state.Top;
		try
		{
			LuaGlobalPushOutcome resolution = LuaGlobalFunctions.TryPushWithOutcome(state, cache, globalName);
			if (!resolution.IsSuccess)
			{
				return resolution.ToOperationStatus();
			}

			LuaStatus status = state.TryCall(0, 1);
			if (!status.IsOk)
			{
				return LuaOperationStatus.LuaFailure(status);
			}

			if (state.TypeOf(-1) != LuaType.Boolean)
			{
				return state.IsNil(-1) ? LuaOperationStatus.NilResult : LuaOperationStatus.InvalidResult;
			}

			value = state.ToBoolean(-1);
			return LuaOperationStatus.Success;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static LuaOperationStatus TryReadFileVersion(LuaState state, out CheatEngineVersion version)
	{
		version = default;
		int top = state.Top;
		try
		{
			LuaGlobalPushOutcome resolution =
				LuaGlobalFunctions.TryPushWithOutcome(state, SGetCheatEngineFileVersion, "getCheatEngineFileVersion"u8);
			if (!resolution.IsSuccess)
			{
				return resolution.ToOperationStatus();
			}

			// Two results: CE returns (packed integer, table) or nothing at all; missing results are padded with nil.
			LuaStatus status = state.TryCall(0, 2);
			if (!status.IsOk)
			{
				return LuaOperationStatus.LuaFailure(status);
			}

			int packedIndex = top + 1;
			int tableIndex = top + 2;
			if (state.IsNil(packedIndex))
			{
				return LuaOperationStatus.NilResult;
			}

			if (!state.IsInteger(packedIndex) || !state.TryReadInteger(packedIndex, out long packed) ||
				!RuntimeInfo.TryDecodeFileVersion(packed, out CheatEngineVersion decoded))
			{
				return LuaOperationStatus.InvalidResult;
			}

			if (!state.IsNil(tableIndex))
			{
				if (!state.IsTable(tableIndex))
				{
					return LuaOperationStatus.InvalidResult;
				}

				status = MatchesVersionTable(state, tableIndex, decoded, out bool matches);
				if (!status.IsOk)
				{
					return LuaOperationStatus.LuaFailure(status);
				}

				if (!matches)
				{
					return LuaOperationStatus.InvalidResult;
				}
			}

			version = decoded;
			return LuaOperationStatus.Success;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static LuaStatus MatchesVersionTable(LuaState state, int tableIndex, CheatEngineVersion expected,
		out bool matches)
	{
		matches = false;
		LuaStatus status = TryRawGetIntegerField(state, tableIndex, "major"u8, out long? major);
		if (!status.IsOk || major != expected.Major)
		{
			return status;
		}

		status = TryRawGetIntegerField(state, tableIndex, "minor"u8, out long? minor);
		if (!status.IsOk || minor != expected.Minor)
		{
			return status;
		}

		status = TryRawGetIntegerField(state, tableIndex, "release"u8, out long? release);
		if (!status.IsOk || release != expected.Release)
		{
			return status;
		}

		status = TryRawGetIntegerField(state, tableIndex, "build"u8, out long? build);
		matches = status.IsOk && build == expected.Build;
		return status;
	}

	// Reads t[field] without metamethods: CE builds this table itself. A non-integer field yields null.
	private static LuaStatus TryRawGetIntegerField(LuaState state, int tableIndex, ReadOnlySpan<byte> field,
		out long? value)
	{
		value = null;
		int top = state.Top;
		LuaStatus status = state.TryPushString(field);
		if (!status.IsOk)
		{
			state.SetTop(top);
			return status;
		}

		_ = state.RawGet(tableIndex);
		if (state.IsInteger(-1) && state.TryReadInteger(-1, out long raw))
		{
			value = raw;
		}

		state.SetTop(top);
		return LuaStatus.Ok;
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
