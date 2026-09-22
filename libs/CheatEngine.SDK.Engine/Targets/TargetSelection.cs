using System;
using System.ComponentModel;
using System.Diagnostics;

using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>Observes and validates the target currently selected by Cheat Engine.</summary>
/// <remarks>
///     This API never selects a process and never opens a replacement target for cleanup. It can prove only the current
///     PID plus the current local creation-time observation. It does not claim to have observed an external A-to-B-to-A
///     transition that completed between two observations; a host transition sequence or target-specific host primitive
///     remains a separate qualification requirement for that stronger guarantee.
/// </remarks>
public static class TargetSelection
{
	private static readonly LuaRef SGetOpenedProcessId = new();

	/// <summary>Gets a copied observation of Cheat Engine's current target selection.</summary>
	public static TargetSelectionObservation ObserveCurrent()
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			return ObserveCurrent(state);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Checks whether the current qualified selection still denotes <paramref name="expected" />.</summary>
	/// <param name="expected">The incarnation captured when the target-bound owner was acquired.</param>
	/// <returns>A factual current, changed, reused, or unavailable result.</returns>
	public static TargetIdentityCheck ValidateCurrent(TargetProcessIncarnation expected)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			return ValidateCurrent(state, expected);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	internal static TargetSelectionObservation ObserveCurrent(LuaState state)
	{
		LuaGlobalPushOutcome global =
			LuaGlobalFunctions.TryPushWithOutcome(state, SGetOpenedProcessId, "getOpenedProcessID"u8);
		if (!global.IsSuccess)
		{
			return global.Status == LuaGlobalPushStatus.Unavailable
				? TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.GlobalUnavailable)
				: TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.LuaFailure);
		}

		if (!state.TryCall(0, 1).IsOk)
		{
			return TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.LuaFailure);
		}

		if (!state.TryReadInteger(-1, out long rawProcessId))
		{
			return TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.InvalidResult);
		}

		if (rawProcessId == 0)
		{
			return TargetSelectionObservation.NoTarget();
		}

		if (rawProcessId < 0 || rawProcessId > int.MaxValue)
		{
			return TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.InvalidResult);
		}

		int processId = (int) rawProcessId;
		return TryObserveIncarnation(processId, out TargetProcessIncarnation incarnation)
			? TargetSelectionObservation.Qualified(incarnation)
			: TargetSelectionObservation.Unqualified(processId);
	}

	internal static TargetIdentityCheck ValidateCurrent(LuaState state, TargetProcessIncarnation expected)
	{
		TargetSelectionObservation observed = ObserveCurrent(state);
		if (!observed.IsQualified)
		{
			return CreateUnavailableCheck(observed);
		}

		TargetProcessIncarnation current = observed.Incarnation.GetValueOrDefault();
		if (current.ProcessId != expected.ProcessId)
		{
			return new TargetIdentityCheck(TargetIdentityCheckKind.TargetChanged, observed);
		}

		return current.StartedAtUtcTicks == expected.StartedAtUtcTicks
			? new TargetIdentityCheck(TargetIdentityCheckKind.Current, observed)
			: new TargetIdentityCheck(TargetIdentityCheckKind.ProcessReused, observed);
	}

	internal static TargetIdentityCheck CreateUnavailableCheck(TargetSelectionObservation observation)
	{
		return new TargetIdentityCheck(MapUnavailable(observation.Status), observation);
	}

	private static bool TryObserveIncarnation(int processId, out TargetProcessIncarnation incarnation)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			long startedAtUtcTicks = process.StartTime.ToUniversalTime().Ticks;
			if (startedAtUtcTicks <= 0)
			{
				incarnation = default;
				return false;
			}

			incarnation = new TargetProcessIncarnation(processId, startedAtUtcTicks);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception
			                                  or NotSupportedException or UnauthorizedAccessException)
		{
			incarnation = default;
			return false;
		}
	}

	private static TargetIdentityCheckKind MapUnavailable(TargetSelectionObservationStatus status)
	{
		return status switch
		{
			TargetSelectionObservationStatus.NoTargetSelected => TargetIdentityCheckKind.NoTargetSelected,
			TargetSelectionObservationStatus.CurrentTargetUnqualified => TargetIdentityCheckKind
				.CurrentTargetUnqualified,
			TargetSelectionObservationStatus.GlobalUnavailable => TargetIdentityCheckKind.GlobalUnavailable,
			TargetSelectionObservationStatus.LuaFailure => TargetIdentityCheckKind.LuaFailure,
			_ => TargetIdentityCheckKind.InvalidResult
		};
	}
}
