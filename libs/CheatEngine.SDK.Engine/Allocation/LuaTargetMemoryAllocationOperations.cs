using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
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
public sealed class LuaTargetMemoryAllocationOperations : ITargetMemoryAllocationOperations
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
        if (request.Size.Value <= 0)
            throw new EngineMarshallingException(AllocateOperation, EngineMarshallingDirection.Argument,
                "a positive allocation size", "a zero or negative allocation size");

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            PushGlobal(state, SAllocateMemory, "allocateMemory"u8, AllocateOperation);
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
            if (!status.IsOk) throw new EngineLuaException(AllocateOperation, status);
            if (state.IsNil(-1))
            {
                address = Address.Zero;
                return false;
            }

            if (!Address.TryRead(state, -1, out address))
                throw new EngineMarshallingException(AllocateOperation, EngineMarshallingDirection.Result,
                    "a target address or nil", "a result that is neither an address nor nil");

            if (address.IsZero) return false;
            return true;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <inheritdoc />
    [RequiresPluginEnabled]
    public bool TryDeallocate(Address address, TargetAllocationSize size)
    {
        if (address.IsZero)
            throw new EngineMarshallingException(DeallocateOperation, EngineMarshallingDirection.Argument,
                "a nonzero target address", "the null target address");
        if (size.Value <= 0)
            throw new EngineMarshallingException(DeallocateOperation, EngineMarshallingDirection.Argument,
                "a positive allocation size", "a zero or negative allocation size");

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            PushGlobal(state, SDeallocate, "deAlloc"u8, DeallocateOperation);
            Address.Push(state, address);
            state.PushInteger(size.Value);
            var status = state.TryCall(2, 1);
            if (!status.IsOk) throw new EngineLuaException(DeallocateOperation, status);
            if (state.TypeOf(-1) != LuaType.Boolean)
                throw new EngineMarshallingException(DeallocateOperation, EngineMarshallingDirection.Result,
                    "a Boolean deallocation result", "a non-Boolean result");

            return state.ToBoolean(-1);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static void PushGlobal(LuaState state, LuaRef cache, ReadOnlySpan<byte> name, string operation)
    {
        switch (LuaGlobalFunctions.TryPushWithStatus(state, cache, name))
        {
            case LuaGlobalPushStatus.Success:
                return;
            case LuaGlobalPushStatus.Unavailable:
                throw new EngineGlobalUnavailableException(operation);
            default:
                throw new EngineLuaException(operation, LuaStatus.RuntimeError);
        }
    }
}
