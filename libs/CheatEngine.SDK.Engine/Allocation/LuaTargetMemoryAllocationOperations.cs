using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The production CE 7.7 binding for target allocation and deallocation through <c>allocateMemory</c> and
///     <c>deAlloc</c>.
/// </summary>
/// <remarks>
///     The binding uses attach-epoch-aware global references, one protected call per operation, and restores the Lua
///     stack on every return or exception. It deliberately does not infer a GUI-thread requirement: the CE 7.7 Lua
///     contract for these two globals provides none. A <see langword="nil" /> allocation result and a <c>false</c>
///     deallocation result are expected operation failures; a result of another shape remains a stable marshalling
///     failure. The class is stateless and may be shared by multiple <see cref="TargetMemoryAllocator" /> instances.
/// </remarks>
public sealed class LuaTargetMemoryAllocationOperations : ITargetMemoryAllocationOperations,
    ITargetMemoryAllocationOutcomeOperations, ITargetBoundMemoryAllocationOperations
{
    private const string AllocateOperation = "TargetMemoryAllocate";
    private const string DeallocateOperation = "TargetMemoryDeallocate";
    private static readonly LuaRef SAllocateMemory = new();
    private static readonly LuaRef SDeallocate = new();

    /// <summary>Gets the shared production implementation.</summary>
    public static LuaTargetMemoryAllocationOperations Instance { get; } = new();

    private LuaTargetMemoryAllocationOperations()
    {
    }

    /// <inheritdoc />
    [RequiresPluginEnabled]
    public bool TryAllocate(TargetAllocationRequest request, out Address address)
    {
        if (request.Size.Value <= 0) ThrowInvalidAllocationSize();

        var outcome = AllocateWithOutcome(request);
        address = outcome.Address;
        return GetAllocationResultOrThrow(outcome);
    }

    /// <inheritdoc />
    [RequiresPluginEnabled]
    public bool TryDeallocate(Address address, TargetAllocationSize size)
    {
        if (address.IsZero) ThrowInvalidDeallocationAddress();
        if (size.Value <= 0) ThrowInvalidAllocationSize(DeallocateOperation);

        return GetDeallocationResultOrThrow(DeallocateWithOutcome(address, size));
    }

    /// <inheritdoc />
    [RequiresPluginEnabled]
    public TargetMemoryAllocationOutcome AllocateWithOutcome(TargetAllocationRequest request)
    {
        if (request.Size.Value <= 0)
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.MarshallingFailure));

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            return AllocateCore(state, request);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <inheritdoc />
    [RequiresPluginEnabled]
    public TargetMemoryOperationOutcome DeallocateWithOutcome(Address address, TargetAllocationSize size)
    {
        if (address.IsZero || size.Value <= 0)
            return TargetMemoryOperationOutcome.Failed(EngineFailureKind.MarshallingFailure);

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            return DeallocateCore(state, address, size);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    TargetMemoryAllocationOutcome ITargetBoundMemoryAllocationOperations.AllocateBoundWithOutcome(
        TargetAllocationRequest request,
        out TargetProcessIncarnation incarnation, out TargetSelectionObservation observation)
    {
        incarnation = default;
        observation = default;
        if (request.Size.Value <= 0)
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.MarshallingFailure));

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            observation = TargetSelection.ObserveCurrent(state);
            if (!observation.IsQualified)
                return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                    EngineFailureKind.TargetIdentityUnavailable));

            var outcome = AllocateCore(state, request);
            incarnation = observation.Incarnation.GetValueOrDefault();
            return outcome;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    bool ITargetBoundMemoryAllocationOperations.TryDeallocateBound(TargetProcessIncarnation expected, Address address,
        TargetAllocationSize size, out TargetIdentityCheck targetCheck)
    {
        var outcome = DeallocateBoundWithOutcomeCore(expected, address, size, out targetCheck);
        return targetCheck.IsCurrent && outcome.IsSuccess;
    }

    TargetMemoryOperationOutcome ITargetBoundMemoryAllocationOperations.DeallocateBoundWithOutcome(
        TargetProcessIncarnation expected, Address address, TargetAllocationSize size, out TargetIdentityCheck targetCheck)
    {
        return DeallocateBoundWithOutcomeCore(expected, address, size, out targetCheck);
    }

    private static TargetMemoryAllocationOutcome AllocateCore(LuaState state, TargetAllocationRequest request)
    {
        var globalOutcome = TryPushGlobal(state, SAllocateMemory, "allocateMemory"u8);
        if (!globalOutcome.IsSuccess) return TargetMemoryAllocationOutcome.Failed(globalOutcome);
        state.PushInteger(request.Size.Value);
        var argumentCount = 1;
        if (request.PreferredBaseAddress.HasValue)
        {
            Address.Push(state, request.PreferredBaseAddress.Value);
            argumentCount++;
        }

        if (request.Protection.HasValue)
        {
            if (!request.PreferredBaseAddress.HasValue)
            {
                state.PushNil();
                argumentCount++;
            }

            state.PushInteger((long)(uint)request.Protection.Value);
            argumentCount++;
        }

        var status = state.TryCall(argumentCount, 1);
        if (!status.IsOk)
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.ProtectedLuaFailure, status));
        if (state.IsNil(-1))
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.ExpectedOperationFailure));

        if (!Address.TryRead(state, -1, out var address))
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.MarshallingFailure));

        return address.IsZero
            ? TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.ExpectedOperationFailure))
            : TargetMemoryAllocationOutcome.Succeeded(address);
    }

    private static TargetMemoryOperationOutcome DeallocateCore(LuaState state, Address address, TargetAllocationSize size)
    {
        var globalOutcome = TryPushGlobal(state, SDeallocate, "deAlloc"u8);
        if (!globalOutcome.IsSuccess) return globalOutcome;
        Address.Push(state, address);
        state.PushInteger(size.Value);
        var status = state.TryCall(2, 1);
        if (!status.IsOk)
            return TargetMemoryOperationOutcome.Failed(EngineFailureKind.ProtectedLuaFailure, status);
        if (state.TypeOf(-1) != LuaType.Boolean)
            return TargetMemoryOperationOutcome.Failed(EngineFailureKind.MarshallingFailure);

        return state.ToBoolean(-1)
            ? TargetMemoryOperationOutcome.Succeeded()
            : TargetMemoryOperationOutcome.Failed(EngineFailureKind.ExpectedOperationFailure);
    }

    private static TargetMemoryOperationOutcome DeallocateBoundWithOutcomeCore(TargetProcessIncarnation expected,
        Address address, TargetAllocationSize size, out TargetIdentityCheck targetCheck)
    {
        targetCheck = default;
        if (address.IsZero || size.Value <= 0)
            return TargetMemoryOperationOutcome.Failed(EngineFailureKind.MarshallingFailure);

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            targetCheck = TargetSelection.ValidateCurrent(state, expected);
            return targetCheck.IsCurrent
                ? DeallocateCore(state, address, size)
                : TargetMemoryOperationOutcome.Failed(GetFailureKind(targetCheck));
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static EngineFailureKind GetFailureKind(TargetIdentityCheck check)
    {
        return check.Kind is TargetIdentityCheckKind.TargetChanged or TargetIdentityCheckKind.ProcessReused
            ? EngineFailureKind.TargetIdentityMismatch
            : EngineFailureKind.TargetIdentityUnavailable;
    }

    private static bool GetAllocationResultOrThrow(TargetMemoryAllocationOutcome outcome)
    {
        if (outcome.Operation.IsSuccess) return true;

        ThrowForOutcome(outcome.Operation, AllocateOperation, isAllocation: true);
        return false;
    }

    private static bool GetDeallocationResultOrThrow(TargetMemoryOperationOutcome outcome)
    {
        if (outcome.IsSuccess) return true;

        ThrowForOutcome(outcome, DeallocateOperation, isAllocation: false);
        return false;
    }

    private static void ThrowForOutcome(TargetMemoryOperationOutcome outcome, string operation, bool isAllocation)
    {
        switch (outcome.Kind)
        {
            case TargetMemoryOperationOutcomeKind.ExpectedFailure:
                return;
            case TargetMemoryOperationOutcomeKind.GlobalUnavailable:
                throw new EngineGlobalUnavailableException(operation);
            case TargetMemoryOperationOutcomeKind.CapabilityUnavailable:
                throw new EngineCapabilityUnavailableException("TargetMemoryAllocation");
            case TargetMemoryOperationOutcomeKind.ProtectedLuaFailure:
                throw new EngineLuaException(operation, outcome.LuaStatus);
            case TargetMemoryOperationOutcomeKind.BindingFailure:
                throw new EngineBindingException(operation);
            case TargetMemoryOperationOutcomeKind.MarshallingFailure:
                if (isAllocation)
                    throw new EngineMarshallingException(operation, EngineMarshallingDirection.Result,
                        "a target address or nil", "a result that is neither an address nor nil");

                throw new EngineMarshallingException(operation, EngineMarshallingDirection.Result,
                    "a Boolean deallocation result", "a non-Boolean result");
            default:
                throw new EngineBindingException(operation);
        }
    }

    private static TargetMemoryOperationOutcome TryPushGlobal(LuaState state, LuaRef cache, ReadOnlySpan<byte> name)
    {
        var resolution = LuaGlobalFunctions.TryPushWithOutcome(state, cache, name);
        return resolution.Status switch
        {
            LuaGlobalPushStatus.Success => TargetMemoryOperationOutcome.Succeeded(),
            LuaGlobalPushStatus.Unavailable => TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.GlobalUnavailable),
            _ => TargetMemoryOperationOutcome.Failed(EngineFailureKind.ProtectedLuaFailure,
                resolution.LuaStatus),
        };
    }

    private static void ThrowInvalidAllocationSize()
    {
        ThrowInvalidAllocationSize(AllocateOperation);
    }

    private static void ThrowInvalidAllocationSize(string operation)
    {
        throw new EngineMarshallingException(operation, EngineMarshallingDirection.Argument,
            "a positive allocation size", "a zero or negative allocation size");
    }

    private static void ThrowInvalidDeallocationAddress()
    {
        throw new EngineMarshallingException(DeallocateOperation, EngineMarshallingDirection.Argument,
            "a nonzero target address", "the null target address");
    }
}
