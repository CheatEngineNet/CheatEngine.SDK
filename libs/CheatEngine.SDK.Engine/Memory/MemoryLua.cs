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
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				value = default;
				return false;
			}

			state.PushInteger(address);
			if (hasSignedArgument)
			{
				state.PushBoolean(signed);
			}

			LuaStatus status = state.TryCall(hasSignedArgument ? 2 : 1, 1);
			if (!status.IsOk)
			{
				return Fail(out value, out failure, MemoryAccessFailure.LuaError);
			}

			// lua_tointegerx accepts strings as a convenience conversion. Memory globals must not: a CE scalar
			// contract is a Lua number, while an integral Lua floating-point result remains a valid scalar.
			if (state.TypeOf(-1) != LuaType.Number || !state.TryReadInteger(-1, out value))
			{
				return Fail(out value, out failure,
					state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);
			}

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
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				value = default;
				return false;
			}

			state.PushInteger(address);
			LuaStatus status = state.TryCall(1, 1);
			if (!status.IsOk)
			{
				return Fail(out value, out failure, MemoryAccessFailure.LuaError);
			}

			if (state.TypeOf(-1) != LuaType.Number || !state.TryReadNumber(-1, out value))
			{
				return Fail(out value, out failure,
					state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);
			}

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
		return TryReadUtf8(cache, name, address, maximumLength, wideCharacter, destination, out written, out _,
			out failure);
	}

	internal static bool TryReadUtf8(LuaRef cache, ReadOnlySpan<byte> name, long address, int maximumLength,
		bool wideCharacter, Span<byte> destination, out int written, out int requiredLength,
		out MemoryAccessFailure failure)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				written = 0;
				requiredLength = 0;
				return false;
			}

			state.PushInteger(address);
			state.PushInteger(maximumLength);
			state.PushBoolean(wideCharacter);
			LuaStatus status = state.TryCall(3, 1);
			if (!status.IsOk)
			{
				written = 0;
				requiredLength = 0;
				failure = MemoryAccessFailure.LuaError;
				return false;
			}

			if (!state.TryReadUtf8(-1, out ReadOnlySpan<byte> utf8))
			{
				written = 0;
				requiredLength = 0;
				failure = state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult;
				return false;
			}

			requiredLength = utf8.Length;
			if (utf8.Length > destination.Length)
			{
				written = 0;
				failure = MemoryAccessFailure.DestinationTooSmall;
				return false;
			}

			utf8.CopyTo(destination);
			written = requiredLength;

			failure = MemoryAccessFailure.None;
			return true;
		}
		catch (LuaException)
		{
			written = 0;
			requiredLength = 0;
			failure = MemoryAccessFailure.LuaError;
			return false;
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
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				value = default;
				return false;
			}

			state.PushInteger(address);
			state.PushInteger(maximumLength);
			state.PushBoolean(wideCharacter);
			LuaStatus status = state.TryCall(3, 1);
			if (!status.IsOk)
			{
				return Fail(out value, out failure, MemoryAccessFailure.LuaError);
			}

			if (!state.TryReadString(-1, out value))
			{
				return Fail(out value, out failure,
					state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);
			}

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
		return TryReadBytesCore(cache, name, address, destination, false, out _, out failure);
	}

	internal static bool TryReadBytes(LuaRef cache, ReadOnlySpan<byte> name, long address, Span<byte> destination,
		out int written, out MemoryAccessFailure failure)
	{
		return TryReadBytesCore(cache, name, address, destination, true, out written, out failure);
	}

	private static bool TryReadBytesCore(LuaRef cache, ReadOnlySpan<byte> name, long address, Span<byte> destination,
		bool copyPartial, out int written, out MemoryAccessFailure failure)
	{
		written = 0;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		if (destination.IsEmpty)
		{
			failure = MemoryAccessFailure.None;
			return true;
		}

		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				written = 0;
				return false;
			}

			state.PushInteger(address);
			state.PushInteger(destination.Length);
			state.PushBoolean(true);
			LuaStatus status = state.TryCall(3, 1);
			if (!status.IsOk)
			{
				return Fail(out written, out failure, MemoryAccessFailure.LuaError);
			}

			if (!state.IsTable(-1))
			{
				return Fail(out written, out failure,
					state.IsNil(-1) ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);
			}

			int table = state.AbsoluteIndex(-1);
			if (copyPartial)
			{
				return TryCopyPartialBytes(state, table, destination, out written, out failure);
			}

			return TryCopyCompleteBytes(state, table, destination, out written, out failure);
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

	private static bool TryCopyCompleteBytes(LuaState state, int table, Span<byte> destination, out int written,
		out MemoryAccessFailure failure)
	{
		for (int index = 0; index < destination.Length; index++)
		{
			LuaType type = state.RawGetIndex(table, index + 1L);
			bool valid = type == LuaType.Number && state.TryReadInteger(-1, out long value)
			                                    && (ulong) value <= byte.MaxValue;
			state.Pop(1);
			if (!valid)
			{
				return Fail(out written, out failure,
					type == LuaType.Nil ? MemoryAccessFailure.ReadFailed : MemoryAccessFailure.InvalidResult);
			}
		}

		for (int index = 0; index < destination.Length; index++)
		{
			state.RawGetIndex(table, index + 1L);
			_ = state.TryReadInteger(-1, out long value);
			state.Pop(1);
			destination[index] = (byte) value;
		}

		written = destination.Length;
		failure = MemoryAccessFailure.None;
		return true;
	}

	private static bool TryCopyPartialBytes(LuaState state, int table, Span<byte> destination, out int written,
		out MemoryAccessFailure failure)
	{
		written = 0;
		for (int index = 0; index < destination.Length; index++)
		{
			LuaType type = state.RawGetIndex(table, index + 1L);
			long value = default;
			bool valid = type == LuaType.Number && state.TryReadInteger(-1, out value)
			                                    && (ulong) value <= byte.MaxValue;
			state.Pop(1);
			if (!valid)
			{
				failure = type == LuaType.Nil && written != 0
					? MemoryAccessFailure.PartialRead
					: type == LuaType.Nil
						? MemoryAccessFailure.ReadFailed
						: MemoryAccessFailure.InvalidResult;
				return false;
			}

			destination[index] = (byte) value;
			written = index + 1;
		}

		failure = MemoryAccessFailure.None;
		return true;
	}

	internal static bool TryWriteInteger(LuaRef cache, ReadOnlySpan<byte> name, long address, long value,
		out MemoryAccessFailure failure)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				return false;
			}

			state.PushInteger(address);
			state.PushInteger(value);
			LuaStatus status = state.TryCall(2, 1);
			if (!status.IsOk)
			{
				return Fail(out failure, MemoryAccessFailure.LuaError);
			}

			if (state.TypeOf(-1) != LuaType.Boolean)
			{
				return Fail(out failure, MemoryAccessFailure.InvalidResult);
			}

			if (!state.ToBoolean(-1))
			{
				return Fail(out failure, MemoryAccessFailure.WriteFailed);
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

	internal static bool TryWriteNumber(LuaRef cache, ReadOnlySpan<byte> name, long address, double value,
		out MemoryAccessFailure failure)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				return false;
			}

			state.PushInteger(address);
			state.PushNumber(value);
			LuaStatus status = state.TryCall(2, 1);
			if (!status.IsOk)
			{
				return Fail(out failure, MemoryAccessFailure.LuaError);
			}

			if (state.TypeOf(-1) != LuaType.Boolean)
			{
				return Fail(out failure, MemoryAccessFailure.InvalidResult);
			}

			if (!state.ToBoolean(-1))
			{
				return Fail(out failure, MemoryAccessFailure.WriteFailed);
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

	internal static bool TryWriteUtf8(LuaRef cache, ReadOnlySpan<byte> name, long address, ReadOnlySpan<byte> value,
		bool wideCharacter, out MemoryAccessFailure failure)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				return false;
			}

			state.PushInteger(address);
			state.PushString(value);
			state.PushBoolean(wideCharacter);
			LuaStatus status = state.TryCall(3, 1);
			if (!status.IsOk)
			{
				return Fail(out failure, MemoryAccessFailure.LuaError);
			}

			if (state.TypeOf(-1) != LuaType.Boolean)
			{
				return Fail(out failure, MemoryAccessFailure.InvalidResult);
			}

			if (!state.ToBoolean(-1))
			{
				return Fail(out failure, MemoryAccessFailure.WriteFailed);
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

	internal static bool TryWriteText(LuaRef cache, ReadOnlySpan<byte> name, long address, ReadOnlySpan<char> value,
		bool wideCharacter, out MemoryAccessFailure failure)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				return false;
			}

			state.PushInteger(address);
			state.PushString(value);
			state.PushBoolean(wideCharacter);
			LuaStatus status = state.TryCall(3, 1);
			if (!status.IsOk)
			{
				return Fail(out failure, MemoryAccessFailure.LuaError);
			}

			if (state.TypeOf(-1) != LuaType.Boolean)
			{
				return Fail(out failure, MemoryAccessFailure.InvalidResult);
			}

			if (!state.ToBoolean(-1))
			{
				return Fail(out failure, MemoryAccessFailure.WriteFailed);
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

	internal static bool TryWriteBytes(LuaRef cache, ReadOnlySpan<byte> name, long address, ReadOnlySpan<byte> value,
		out MemoryAccessFailure failure)
	{
		return TryWriteBytes(cache, name, address, value, out _, out failure);
	}

	internal static bool TryWriteBytes(LuaRef cache, ReadOnlySpan<byte> name, long address, ReadOnlySpan<byte> value,
		out int written, out MemoryAccessFailure failure)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();

		if (value.IsEmpty)
		{
			written = 0;
			failure = MemoryAccessFailure.None;
			return true;
		}

		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!TryPushGlobal(state, cache, name, out failure))
			{
				written = 0;
				return false;
			}

			state.PushInteger(address);
			state.PushByteTable(value);

			LuaStatus status = state.TryCall(2, 1);
			if (!status.IsOk)
			{
				return Fail(out written, out failure, MemoryAccessFailure.LuaError);
			}

			if (state.TypeOf(-1) != LuaType.Number || !state.TryReadInteger(-1, out long reported))
			{
				return Fail(out written, out failure, MemoryAccessFailure.InvalidResult);
			}

			if (reported < 0 || reported > value.Length)
			{
				return Fail(out written, out failure, MemoryAccessFailure.InvalidResult);
			}

			written = (int) reported;
			if (written != value.Length)
			{
				failure = MemoryAccessFailure.WriteFailed;
				return false;
			}

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

	private static bool Fail(out MemoryAccessFailure failure, MemoryAccessFailure value)
	{
		failure = value;
		return false;
	}

	private static bool TryPushGlobal(LuaState state, LuaRef cache, ReadOnlySpan<byte> name,
		out MemoryAccessFailure failure)
	{
		failure = LuaGlobalFunctions.TryPushWithStatus(state, cache, name) switch
		{
			LuaGlobalPushStatus.Success => MemoryAccessFailure.None,
			LuaGlobalPushStatus.Unavailable => MemoryAccessFailure.GlobalUnavailable,
			_ => MemoryAccessFailure.LuaError
		};
		return failure == MemoryAccessFailure.None;
	}

	private static bool Fail<T>(out T value, out MemoryAccessFailure failure, MemoryAccessFailure failureValue)
	{
		value = default!;
		failure = failureValue;
		return false;
	}
}
