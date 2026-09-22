using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Observes CE's current selected process identifier, architecture probes, and instruction address width.</summary>
/// <remarks>
///     <para>
///         The observation calls <c>getOpenedProcessID</c>, <c>targetIs64Bit</c>, <c>targetIsX86</c>, and
///         <c>targetIsArm</c> under one Lua runtime admission, then checks the process identifier a second time. It
///         rejects contradictory architecture facts rather than inferring an ISA from a pointer width or from the
///         managed host.
///     </para>
///     <para>
///         CE exposes ambient target state rather than a target-selection lock. The repeated identifier makes a
///         selection transition observable during this operation, but it does not identify a process incarnation or
///         prove that a later assembler or disassembler call runs against the same target. Consumers must retain
///         the resulting <see cref="InstructionTargetProfile" /> and pass it to an instruction operation, which
///         performs the same
///         before-and-after coherence check.
///     </para>
/// </remarks>
public static class InstructionProfiles
{
	private static readonly LuaRef SGetOpenedProcessId = new();
	private static readonly LuaRef STargetIs64Bit = new();
	private static readonly LuaRef STargetIsX86 = new();
	private static readonly LuaRef STargetIsArm = new();

	/// <summary>Observes the current CE target and returns a profile only when its selected PID and ISA probes agree.</summary>
	/// <param name="targetProfile">The copied profile and selected PID only when the returned status is success.</param>
	/// <returns>A target, availability, protected-Lua, or profile-consistency outcome.</returns>
	/// <exception cref="global::System.InvalidOperationException">
	///     The plugin is not enabled or the calling thread has no Lua
	///     state.
	/// </exception>
	[RequiresPluginEnabled]
	public static InstructionOperationStatus TryObserveCurrent(out InstructionTargetProfile targetProfile)
	{
		targetProfile = default;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			InstructionOperationStatus status = TryGetCurrentTarget(state, out TargetProcessId firstTarget);
			if (status != InstructionOperationStatus.Success)
			{
				return status;
			}

			status = TryCallBoolean(state, STargetIs64Bit, "targetIs64Bit"u8, out bool is64Bit);
			if (status != InstructionOperationStatus.Success)
			{
				return status;
			}

			status = TryCallBoolean(state, STargetIsX86, "targetIsX86"u8, out bool isX86);
			if (status != InstructionOperationStatus.Success)
			{
				return status;
			}

			status = TryCallBoolean(state, STargetIsArm, "targetIsArm"u8, out bool isArm);
			if (status != InstructionOperationStatus.Success)
			{
				return status;
			}

			if (!TryCreateProfile(is64Bit, isX86, isArm, out InstructionProfile profile))
			{
				return InstructionOperationStatus.InvalidProfile;
			}

			status = TryGetCurrentTarget(state, out TargetProcessId finalTarget);
			if (status != InstructionOperationStatus.Success)
			{
				return status;
			}

			if (firstTarget != finalTarget)
			{
				return InstructionOperationStatus.TargetChanged;
			}

			targetProfile = new InstructionTargetProfile(firstTarget, profile);
			return InstructionOperationStatus.Success;
		}
		catch (LuaException)
		{
			targetProfile = default;
			return InstructionOperationStatus.LuaFailure;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	internal static InstructionOperationStatus TryVerifyCurrent(LuaState state, TargetProcessId expectedTarget)
	{
		InstructionOperationStatus status = TryGetCurrentTarget(state, out TargetProcessId actualTarget);
		return status == InstructionOperationStatus.Success && actualTarget != expectedTarget
			? InstructionOperationStatus.TargetChanged
			: status;
	}

	private static InstructionOperationStatus TryGetCurrentTarget(LuaState state, out TargetProcessId target)
	{
		target = default;
		InstructionOperationStatus status = TryPushGlobal(state, SGetOpenedProcessId, "getOpenedProcessID"u8);
		if (status != InstructionOperationStatus.Success)
		{
			return status;
		}

		if (!state.TryCall(0, 1).IsOk)
		{
			return InstructionOperationStatus.LuaFailure;
		}

		if (state.TypeOf(-1) != LuaType.Number || !state.TryReadInteger(-1, out long value) ||
		    value is < 0 or > int.MaxValue)
		{
			return InstructionOperationStatus.InvalidResult;
		}

		if (value == 0)
		{
			return InstructionOperationStatus.TargetNotSelected;
		}

		target = new TargetProcessId((int) value);
		return InstructionOperationStatus.Success;
	}

	private static InstructionOperationStatus TryCallBoolean(LuaState state, LuaRef cache, ReadOnlySpan<byte> name,
		out bool value)
	{
		value = default;
		InstructionOperationStatus status = TryPushGlobal(state, cache, name);
		if (status != InstructionOperationStatus.Success)
		{
			return status;
		}

		if (!state.TryCall(0, 1).IsOk)
		{
			return InstructionOperationStatus.LuaFailure;
		}

		if (state.TypeOf(-1) != LuaType.Boolean)
		{
			return InstructionOperationStatus.InvalidResult;
		}

		value = state.ToBoolean(-1);
		return InstructionOperationStatus.Success;
	}

	private static InstructionOperationStatus TryPushGlobal(LuaState state, LuaRef cache, ReadOnlySpan<byte> name)
	{
		return LuaGlobalFunctions.TryPushWithStatus(state, cache, name) switch
		{
			LuaGlobalPushStatus.Success => InstructionOperationStatus.Success,
			LuaGlobalPushStatus.Unavailable => InstructionOperationStatus.GlobalUnavailable,
			_ => InstructionOperationStatus.LuaFailure
		};
	}

	private static bool TryCreateProfile(bool is64Bit, bool isX86, bool isArm, out InstructionProfile profile)
	{
		if (isX86 && isArm)
		{
			profile = default;
			return false;
		}

		if (isX86)
		{
			profile = is64Bit ? default : InstructionProfile.X86;
			return !is64Bit;
		}

		if (isArm)
		{
			profile = is64Bit ? InstructionProfile.Arm64 : InstructionProfile.Arm32;
			return true;
		}

		if (is64Bit)
		{
			profile = InstructionProfile.X64;
			return true;
		}

		profile = default;
		return false;
	}
}
