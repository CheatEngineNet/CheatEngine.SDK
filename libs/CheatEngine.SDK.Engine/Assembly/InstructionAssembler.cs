using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Assembles one instruction through CE's protected Lua <c>assemble</c> global.</summary>
/// <remarks>
///     <para>
///         The CE 7.7.0.10621 implementation accepts the instruction text and optional address, then returns a Lua
///         byte table on success or <c>nil</c> when it rejects the instruction. This API always supplies the address:
///         it is the explicit origin CE receives when it evaluates a relative operand. It does not claim relocation
///         support beyond that CE operation or reconfigure CE's ambient target architecture.
///     </para>
///     <para>
///         The destination is caller-owned. The table is validated completely before the first byte is copied, so a
///         short destination or malformed later element cannot publish a prefix. Neither the table nor Lua text is
///         retained after the stack is restored. This API does not invoke a classic native assembler slot; unresolved
///         host ABI projections remain unavailable.
///     </para>
/// </remarks>
public static class InstructionAssembler
{
    private static readonly LuaRef SAssemble = new();

    /// <summary>Assembles one instruction into caller-owned storage using an explicit target address as its origin.</summary>
    /// <param name="targetProfile">The CE-observed selected PID and instruction profile used to validate <paramref name="address" />.</param>
    /// <param name="instruction">The instruction source sent to CE as UTF-8 without normalization.</param>
    /// <param name="address">The target origin supplied to CE; relative operands are interpreted by CE relative to this address.</param>
    /// <param name="destination">Caller-owned storage for the complete assembled byte sequence.</param>
    /// <param name="written">The number of copied bytes on success; zero for every other outcome.</param>
    /// <param name="requiredLength">The exact byte-table length when CE returned a valid table; zero otherwise.</param>
    /// <returns>A profile, capacity, instruction, availability, protected-call, or result-shape outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instruction" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    [SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
        Justification = "The protected call, full table validation, target recheck, and copy share one stack frame.")]
    public static InstructionOperationStatus TryAssemble(InstructionTargetProfile targetProfile, string instruction, Address address,
        Span<byte> destination, out int written, out int requiredLength)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        written = 0;
        requiredLength = 0;

        var profileStatus = targetProfile.Validate(address);
        if (profileStatus != InstructionOperationStatus.Success) return profileStatus;

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        try
        {
            var targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
            if (targetStatus != InstructionOperationStatus.Success) return targetStatus;

            var global = LuaGlobalFunctions.TryPushWithStatus(state, SAssemble, "assemble"u8);
            if (global == LuaGlobalPushStatus.Unavailable) return InstructionOperationStatus.GlobalUnavailable;
            if (global != LuaGlobalPushStatus.Success) return InstructionOperationStatus.LuaFailure;

            var resultStart = state.Top - 1;
            StringMarshaller.Push(state, instruction);
            Address.Push(state, address);
            if (!state.TryCall(2, LuaState.MultipleResults).IsOk) return InstructionOperationStatus.LuaFailure;

            targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
            if (targetStatus != InstructionOperationStatus.Success) return targetStatus;

            var resultIndex = resultStart + 1;
            if (state.Top < resultIndex) return InstructionOperationStatus.InvalidResult;
            if (state.IsNil(resultIndex)) return InstructionOperationStatus.InstructionRejected;
            if (!state.IsTable(resultIndex)) return InstructionOperationStatus.InvalidResult;

            var tableIndex = state.AbsoluteIndex(resultIndex);
            var rawLength = state.RawLength(tableIndex);
            if (rawLength > (nuint)int.MaxValue) return InstructionOperationStatus.InvalidResult;
            requiredLength = (int)rawLength;
            if (requiredLength > destination.Length) return InstructionOperationStatus.DestinationTooSmall;

            if (!ValidateByteTable(state, tableIndex, requiredLength))
            {
                requiredLength = 0;
                return InstructionOperationStatus.InvalidResult;
            }

            targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
            if (targetStatus != InstructionOperationStatus.Success)
            {
                requiredLength = 0;
                return targetStatus;
            }

            for (var index = 0; index < requiredLength; index++)
            {
                _ = state.RawGetSequenceItem(tableIndex, index);
                _ = state.TryReadInteger(-1, out var value);
                destination[index] = (byte)value;
                state.Pop(1);
            }

            written = requiredLength;
            return InstructionOperationStatus.Success;
        }
        catch (LuaException)
        {
            written = 0;
            requiredLength = 0;
            return InstructionOperationStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static bool ValidateByteTable(LuaState state, int tableIndex, int length)
    {
        for (var index = 0; index < length; index++)
        {
            _ = state.RawGetSequenceItem(tableIndex, index);
            var valid = state.TryReadInteger(-1, out var value) && value is >= byte.MinValue and <= byte.MaxValue;
            state.Pop(1);
            if (!valid) return false;
        }

        return true;
    }
}
