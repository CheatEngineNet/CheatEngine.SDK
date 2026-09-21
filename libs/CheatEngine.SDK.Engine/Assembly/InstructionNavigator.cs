using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Reads instruction size and CE's estimated previous opcode without exposing a native navigation slot.</summary>
/// <remarks>
///     CE registers <c>getInstructionSize</c> and <c>getPreviousOpcode</c> as separate Lua globals. The latter is an
///     estimate in CE's own Lua documentation: variable-length instruction streams cannot in general be walked
///     backwards from one address without prior decoding context. These methods return copied values only; they retain
///     no CE instruction object, display text, native pointer, or Lua reference. In particular, they do not call the
///     conflicting classic <c>previousOpcode</c>/<c>nextOpcode</c> slots.
/// </remarks>
public static class InstructionNavigator
{
    private static readonly LuaRef SGetInstructionSize = new();
    private static readonly LuaRef SGetPreviousOpcode = new();

    /// <summary>Gets CE's positive byte length for the instruction that starts at <paramref name="address" />.</summary>
    /// <param name="targetProfile">The CE-observed selected PID and instruction profile used to validate the address.</param>
    /// <param name="address">The target address supplied to CE's <c>getInstructionSize</c> global.</param>
    /// <param name="length">The positive byte length only when the status is <see cref="InstructionOperationStatus.Success" />.</param>
    /// <returns>A profile, availability, protected-call, or result-shape outcome.</returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InstructionOperationStatus TryGetLength(InstructionTargetProfile targetProfile, Address address, out int length)
    {
        length = 0;
        var profileStatus = targetProfile.Validate(address);
        if (profileStatus != InstructionOperationStatus.Success) return profileStatus;

        var status = TryCallAddressGlobal(targetProfile, SGetInstructionSize, "getInstructionSize"u8, address, out var value);
        if (status != InstructionOperationStatus.Success) return status;
        if (value is <= 0 or > int.MaxValue) return InstructionOperationStatus.InvalidResult;

        length = (int)value;
        return InstructionOperationStatus.Success;
    }

    /// <summary>Gets CE's estimated previous opcode address for a profile-qualified target address.</summary>
    /// <param name="targetProfile">The CE-observed selected PID and instruction profile used to validate input and output addresses.</param>
    /// <param name="address">The target address supplied to CE's <c>getPreviousOpcode</c> global.</param>
    /// <param name="previous">The estimated target address only when the status is <see cref="InstructionOperationStatus.Success" />.</param>
    /// <returns>A profile, availability, protected-call, or result-shape outcome.</returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InstructionOperationStatus TryGetPrevious(InstructionTargetProfile targetProfile, Address address, out Address previous)
    {
        previous = Address.Zero;
        var profileStatus = targetProfile.Validate(address);
        if (profileStatus != InstructionOperationStatus.Success) return profileStatus;

        var status = TryCallAddressGlobal(targetProfile, SGetPreviousOpcode, "getPreviousOpcode"u8, address, out var value);
        if (status != InstructionOperationStatus.Success) return status;

        var candidate = Address.FromInt64(value);
        if (targetProfile.Validate(candidate) != InstructionOperationStatus.Success)
            return InstructionOperationStatus.AddressExceedsProfileWidth;

        previous = candidate;
        return InstructionOperationStatus.Success;
    }

    private static InstructionOperationStatus TryCallAddressGlobal(InstructionTargetProfile targetProfile, LuaRef cache,
        ReadOnlySpan<byte> name, Address address,
        out long value)
    {
        value = 0;
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            var targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
            if (targetStatus != InstructionOperationStatus.Success) return targetStatus;

            var global = LuaGlobalFunctions.TryPushWithStatus(state, cache, name);
            if (global == LuaGlobalPushStatus.Unavailable) return InstructionOperationStatus.GlobalUnavailable;
            if (global != LuaGlobalPushStatus.Success) return InstructionOperationStatus.LuaFailure;

            Address.Push(state, address);
            if (!state.TryCall(1, 1).IsOk) return InstructionOperationStatus.LuaFailure;
            if (state.TypeOf(-1) != LuaType.Number || !state.TryReadInteger(-1, out value))
                return InstructionOperationStatus.InvalidResult;

            targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
            if (targetStatus != InstructionOperationStatus.Success)
            {
                value = 0;
                return targetStatus;
            }

            return InstructionOperationStatus.Success;
        }
        catch (LuaException)
        {
            value = 0;
            return InstructionOperationStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }
}
