using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>Protected CE 7.7 operations for observing and explicitly selecting the current target process.</summary>
/// <remarks>
///     The operations use <c>getOpenedProcessID</c>, <c>openProcess</c>, and <c>targetIs64Bit</c>. They preserve
///     absence, unavailable-global, protected-Lua, malformed-result, and unconfirmed-selection outcomes without
///     parsing Lua error text. They do not provide name lookup, process enumeration, automatic selection, OS-handle
///     ownership, process-lifetime atomicity, target-ISA detection, or a main-thread dispatch guarantee. CE's source
///     catalogue does not prove a GUI-thread requirement for this subset, so calls run on the acquiring thread's
///     host-provided Lua state under one lifecycle admission.
/// </remarks>
public static class RuntimeProcessOperations
{
    private static readonly LuaRef SGetOpenedProcessId = new();
    private static readonly LuaRef SOpenProcess = new();
    private static readonly LuaRef STargetIs64Bit = new();

    /// <summary>Observes the current CE target process and its pointer width.</summary>
    /// <param name="observation">The copied target observation only when the returned status is successful.</param>
    /// <returns>The factual protected process-observation status.</returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
    {
        using var operation = LuaRuntime.AcquireOperation();
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
    [RequiresPluginEnabled]
    public static ProcessOperationStatus SelectAndObserve(TargetProcessId processId,
        out CurrentProcessObservation observation)
    {
        ValidateProcessId(processId);
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            var status = TryOpenProcess(state, processId);
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

            status = TryGetTargetPointerSize(state, out PointerSize pointerSize);
            if (!status.IsSuccess)
            {
                observation = default;
                return status;
            }

            observation = new CurrentProcessObservation(observedProcessId!.Value, pointerSize);
            return ProcessOperationStatus.Success;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static ProcessOperationStatus ObserveCurrent(LuaState state, out CurrentProcessObservation observation)
    {
        var top = state.Top;
        try
        {
            var status = TryGetOpenedProcessId(state, out TargetProcessId? processId);
            if (!status.IsSuccess || !processId.HasValue)
            {
                observation = default;
                return status;
            }

            status = TryGetTargetPointerSize(state, out PointerSize pointerSize);
            if (!status.IsSuccess)
            {
                observation = default;
                return status;
            }

            observation = new CurrentProcessObservation(processId.Value, pointerSize);
            return ProcessOperationStatus.Success;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static ProcessOperationStatus TryGetOpenedProcessId(LuaState state, out TargetProcessId? processId)
    {
        var resolution = LuaGlobalFunctions.TryPushWithOutcome(state, SGetOpenedProcessId, "getOpenedProcessID"u8);
        if (!resolution.IsSuccess)
        {
            processId = default;
            return FromResolution(resolution);
        }

        var luaStatus = state.TryCall(0, 1);
        if (!luaStatus.IsOk)
        {
            processId = default;
            return ProcessOperationStatus.ProtectedLuaFailure(luaStatus);
        }

        if (state.TypeOf(-1) != LuaType.Number || !state.TryReadInteger(-1, out var value) ||
            value is < 0 or > int.MaxValue)
        {
            processId = default;
            return ProcessOperationStatus.InvalidResult;
        }

        if (value == 0)
        {
            processId = default;
            return ProcessOperationStatus.TargetNotAttached;
        }

        processId = new TargetProcessId((int)value);
        return ProcessOperationStatus.Success;
    }

    private static ProcessOperationStatus TryGetTargetPointerSize(LuaState state, out PointerSize pointerSize)
    {
        var status = TryCallBoolean(state, STargetIs64Bit, "targetIs64Bit"u8, out var is64Bit);
        if (!status.IsSuccess)
        {
            pointerSize = PointerSize.Unknown;
            return status;
        }

        pointerSize = is64Bit ? PointerSize.Bit64 : PointerSize.Bit32;
        return ProcessOperationStatus.Success;
    }

    private static ProcessOperationStatus TryOpenProcess(LuaState state, TargetProcessId processId)
    {
        var resolution = LuaGlobalFunctions.TryPushWithOutcome(state, SOpenProcess, "openProcess"u8);
        if (!resolution.IsSuccess)
        {
            return FromResolution(resolution);
        }

        state.PushInteger(processId.Value);
        var luaStatus = state.TryCall(1, 0);
        return luaStatus.IsOk
            ? ProcessOperationStatus.Success
            : ProcessOperationStatus.ProtectedLuaFailure(luaStatus);
    }

    private static ProcessOperationStatus TryCallBoolean(LuaState state, LuaRef cache, ReadOnlySpan<byte> globalName,
        out bool value)
    {
        var resolution = LuaGlobalFunctions.TryPushWithOutcome(state, cache, globalName);
        if (!resolution.IsSuccess)
        {
            value = default;
            return FromResolution(resolution);
        }

        var luaStatus = state.TryCall(0, 1);
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
            _ => ProcessOperationStatus.InvalidResult,
        };
    }

    private static void ValidateProcessId(TargetProcessId processId)
    {
        if (processId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId.Value,
                "A target process identifier must be positive.");
    }
}
