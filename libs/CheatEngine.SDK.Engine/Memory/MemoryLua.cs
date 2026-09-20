using System;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Memory;

// The CE 7.7 memory catalog has scalar calls, strings and byte tables. This internal layer owns the protected call
// shape once so TargetMemory and HostMemory cannot accidentally diverge in stack restoration or failure classification.
internal static class MemoryLua
{
    internal static bool TryReadInteger(LuaRef cache, ReadOnlySpan<byte> name, long address, bool signed,
        bool hasSignedArgument, out long value, out MemoryAccessFailure failure)
    {
        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out value, out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            if (hasSignedArgument) state.PushBoolean(signed);

            LuaStatus status = state.TryCall(hasSignedArgument ? 2 : 1, 1);
            if (!status.IsOk) return Fail(out value, out failure, MemoryAccessFailure.LuaError);
            if (!state.TryReadInteger(-1, out value))
                return Fail(out value, out failure, state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out value, out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryReadNumber(LuaRef cache, ReadOnlySpan<byte> name, long address, out double value,
        out MemoryAccessFailure failure)
    {
        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out value, out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            LuaStatus status = state.TryCall(1, 1);
            if (!status.IsOk) return Fail(out value, out failure, MemoryAccessFailure.LuaError);
            if (!state.TryReadNumber(-1, out value))
                return Fail(out value, out failure, state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out value, out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryReadUtf8(LuaRef cache, ReadOnlySpan<byte> name, long address, int maximumLength,
        bool wideCharacter, Span<byte> destination, out int written, out MemoryAccessFailure failure)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);

        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out written, out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.PushInteger(maximumLength);
            state.PushBoolean(wideCharacter);
            LuaStatus status = state.TryCall(3, 1);
            if (!status.IsOk) return Fail(out written, out failure, MemoryAccessFailure.LuaError);
            if (!state.TryReadUtf8(-1, out ReadOnlySpan<byte> utf8))
                return Fail(out written, out failure, state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);
            if (!utf8.TryCopyTo(destination))
                return Fail(out written, out failure, MemoryAccessFailure.DestinationTooSmall);

            written = utf8.Length;

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out written, out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryReadString(LuaRef cache, ReadOnlySpan<byte> name, long address, int maximumLength,
        bool wideCharacter, out string? value, out MemoryAccessFailure failure)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);

        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out value, out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.PushInteger(maximumLength);
            state.PushBoolean(wideCharacter);
            LuaStatus status = state.TryCall(3, 1);
            if (!status.IsOk) return Fail(out value, out failure, MemoryAccessFailure.LuaError);
            if (!state.TryReadString(-1, out value))
                return Fail(out value, out failure, state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out value, out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryReadBytes(LuaRef cache, ReadOnlySpan<byte> name, long address, Span<byte> destination,
        out MemoryAccessFailure failure)
    {
        if (destination.IsEmpty)
        {
            failure = MemoryAccessFailure.None;
            return true;
        }

        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.PushInteger(destination.Length);
            state.PushBoolean(true);
            LuaStatus status = state.TryCall(3, 1);
            if (!status.IsOk) return Fail(out failure, MemoryAccessFailure.LuaError);
            if (!state.IsTable(-1))
                return Fail(out failure, state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);

            int table = state.AbsoluteIndex(-1);
            for (int index = 0; index < destination.Length; index++)
            {
                LuaType type = state.RawGetIndex(table, index + 1L);
                bool valid = state.TryReadInteger(-1, out long value) && (ulong)value <= byte.MaxValue;
                state.Pop(1);
                if (!valid)
                    return Fail(out failure, type == LuaType.Nil ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);
            }

            for (int index = 0; index < destination.Length; index++)
            {
                state.RawGetIndex(table, index + 1L);
                _ = state.TryReadInteger(-1, out long value);
                state.Pop(1);
                destination[index] = (byte)value;
            }

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryWriteInteger(LuaRef cache, ReadOnlySpan<byte> name, long address, long value,
        out MemoryAccessFailure failure)
    {
        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.PushInteger(value);
            LuaStatus status = state.TryCall(2, 1);
            if (!status.IsOk) return Fail(out failure, MemoryAccessFailure.LuaError);
            if (state.TypeOf(-1) != LuaType.Boolean) return Fail(out failure, MemoryAccessFailure.InvalidResult);
            if (!state.ToBoolean(-1)) return Fail(out failure, MemoryAccessFailure.WriteFailed);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryWriteNumber(LuaRef cache, ReadOnlySpan<byte> name, long address, double value,
        out MemoryAccessFailure failure)
    {
        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.PushNumber(value);
            LuaStatus status = state.TryCall(2, 1);
            if (!status.IsOk) return Fail(out failure, MemoryAccessFailure.LuaError);
            if (state.TypeOf(-1) != LuaType.Boolean) return Fail(out failure, MemoryAccessFailure.InvalidResult);
            if (!state.ToBoolean(-1)) return Fail(out failure, MemoryAccessFailure.WriteFailed);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryWriteUtf8(LuaRef cache, ReadOnlySpan<byte> name, long address, ReadOnlySpan<byte> value,
        bool wideCharacter, out MemoryAccessFailure failure)
    {
        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.PushString(value);
            state.PushBoolean(wideCharacter);
            LuaStatus status = state.TryCall(3, 1);
            if (!status.IsOk) return Fail(out failure, MemoryAccessFailure.LuaError);
            if (state.TypeOf(-1) != LuaType.Boolean) return Fail(out failure, MemoryAccessFailure.InvalidResult);
            if (!state.ToBoolean(-1)) return Fail(out failure, MemoryAccessFailure.WriteFailed);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryWriteText(LuaRef cache, ReadOnlySpan<byte> name, long address, ReadOnlySpan<char> value,
        bool wideCharacter, out MemoryAccessFailure failure)
    {
        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.PushString(value);
            state.PushBoolean(wideCharacter);
            LuaStatus status = state.TryCall(3, 1);
            if (!status.IsOk) return Fail(out failure, MemoryAccessFailure.LuaError);
            if (state.TypeOf(-1) != LuaType.Boolean) return Fail(out failure, MemoryAccessFailure.InvalidResult);
            if (!state.ToBoolean(-1)) return Fail(out failure, MemoryAccessFailure.WriteFailed);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    internal static bool TryWriteBytes(LuaRef cache, ReadOnlySpan<byte> name, long address, ReadOnlySpan<byte> value,
        out MemoryAccessFailure failure)
    {
        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        LuaState state = operation.State;
        int top = state.Top;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, cache, name))
                return Fail(out failure, MemoryAccessFailure.GlobalUnavailable);

            state.PushInteger(address);
            state.CreateTable(value.Length);
            int table = state.AbsoluteIndex(-1);
            for (int index = 0; index < value.Length; index++)
            {
                state.PushInteger(value[index]);
                state.RawSetIndex(table, index + 1L);
            }

            LuaStatus status = state.TryCall(2, 0);
            if (!status.IsOk) return Fail(out failure, MemoryAccessFailure.LuaError);

            failure = MemoryAccessFailure.None;
            return true;
        }
        catch (LuaException)
        {
            return Fail(out failure, MemoryAccessFailure.LuaError);
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static bool Fail(out MemoryAccessFailure failure, MemoryAccessFailure value)
    {
        failure = value;
        return false;
    }

    private static bool Fail<T>(out T value, out MemoryAccessFailure failure, MemoryAccessFailure failureValue)
    {
        value = default!;
        failure = failureValue;
        return false;
    }
}
