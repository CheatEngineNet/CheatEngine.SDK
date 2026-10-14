using System.Globalization;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.FailureProbe;

/// <summary>Deterministic, process-isolated sequences for protected Lua operations and their recovery contracts.</summary>
internal static class ProtectedOperationFuzzProbe
{
	private const uint Seed = 0xD1CE_BA5E;
	private const int SequenceCount = 2000;
	private const int MaximumBufferLength = 1024;
	private const int MaximumCapacity = 64;
	private const int OperationsPerSequence = 10;
	private const long Sentinel = 0x51A7_E11A_600D_0001;

	public static int Run(LuaState state)
	{
		RandomSource random = new(Seed);
		int observedApiCalls = 0;
		for (int iteration = 0; iteration < SequenceCount; iteration++)
		{
			int stringLength = BoundaryOrRandom(ref random, iteration, MaximumBufferLength);
			int tableLength = BoundaryOrRandom(ref random, iteration, MaximumCapacity);
			int capacity = BoundaryOrRandom(ref random, iteration, MaximumCapacity);
			nuint userdataSize = (nuint) BoundaryOrRandom(ref random, iteration, MaximumBufferLength);
			byte[] stringBytes = random.NextBytes(stringLength);
			byte[] tableBytes = random.NextBytes(tableLength);
			long key = random.NextInt32(MaximumCapacity) + 1;
			long value = random.NextInt64();

			if (AssertEmpty(state, iteration, "sequence-start") != 0 ||
				PushBytes(state, stringBytes, iteration, ref observedApiCalls) != 0 ||
				CreateTable(state, capacity, iteration, ref observedApiCalls) != 0 ||
				PushByteTable(state, tableBytes, iteration, ref observedApiCalls) != 0 ||
				CreateUserdata(state, userdataSize, iteration, ref observedApiCalls) != 0 ||
				MutateTable(state, key, value, iteration, ref observedApiCalls) != 0 ||
				RejectInvalidTable(state, key, iteration, ref observedApiCalls) != 0 ||
				RejectAllocation(state, capacity, iteration, ref observedApiCalls) != 0 ||
				Recover(state, iteration, ref observedApiCalls) != 0 ||
				AssertEmpty(state, iteration, "sequence-end") != 0)
			{
				return 1;
			}
		}

		if (observedApiCalls != SequenceCount * OperationsPerSequence)
		{
			return Fail(SequenceCount, "call-budget", "did not execute every protected API partition");
		}

		Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"PASS protected fuzz seed=0x{Seed:X8} sequences={SequenceCount} protected-api-calls={observedApiCalls} buffers<={MaximumBufferLength} capacities<={MaximumCapacity} allocator-refusals={SequenceCount}"));
		return 0;
	}

	private static int BoundaryOrRandom(ref RandomSource random, int iteration, int maximum)
	{
		return (iteration % 3) switch
		{
			0 => 0,
			1 => maximum,
			_ => random.NextInt32(maximum + 1)
		};
	}

	private static int PushBytes(LuaState state, byte[] bytes, int iteration, ref int observedApiCalls)
	{
		LuaStatus status = state.TryPushString(bytes);
		observedApiCalls++;
		if (!status.IsOk || state.Top != 1 || !state.TryReadUtf8(1, out ReadOnlySpan<byte> actual) ||
			!actual.SequenceEqual(bytes))
		{
			return Fail(iteration, "TryPushString", "did not leave the exact successful string as its only stack value");
		}

		state.Pop(1);
		return AssertEmpty(state, iteration, "TryPushString-cleanup");
	}

	private static int CreateTable(LuaState state, int capacity, int iteration, ref int observedApiCalls)
	{
		state.CreateTable(capacity, capacity);
		observedApiCalls++;
		if (state.Top != 1 || !state.IsTable(1))
		{
			return Fail(iteration, "CreateTable", "did not leave one table on the stack");
		}

		state.Pop(1);
		return AssertEmpty(state, iteration, "CreateTable-cleanup");
	}

	private static int PushByteTable(LuaState state, byte[] bytes, int iteration, ref int observedApiCalls)
	{
		state.PushByteTable(bytes);
		observedApiCalls++;
		if (state.Top != 1 || !state.IsTable(1) || state.RawLength(1) != (nuint) bytes.Length)
		{
			return Fail(iteration, "PushByteTable", "did not leave a table with the requested sequence length");
		}

		for (int i = 0; i < bytes.Length; i++)
		{
			if (state.RawGetIndex(1, i + 1) != LuaType.Number || !state.TryReadInteger(-1, out long value) ||
				value != bytes[i])
			{
				return Fail(iteration, "PushByteTable", "table entry did not preserve its source byte");
			}

			state.Pop(1);
		}

		state.Pop(1);
		return AssertEmpty(state, iteration, "PushByteTable-cleanup");
	}

	private static int CreateUserdata(LuaState state, nuint size, int iteration, ref int observedApiCalls)
	{
		nint block = state.NewUserdata(size);
		observedApiCalls++;
		if (block == 0 || state.Top != 1 || !state.IsUserdata(1) || state.RawLength(1) != size)
		{
			return Fail(iteration, "NewUserdata", "did not leave one correctly sized userdata block");
		}

		state.Pop(1);
		return AssertEmpty(state, iteration, "NewUserdata-cleanup");
	}

	private static int MutateTable(LuaState state, long key, long value, int iteration, ref int observedApiCalls)
	{
		state.CreateTable(0, 1);
		observedApiCalls++;
		state.PushInteger(value);
		LuaStatus setStatus = state.TrySetIndex(1, key);
		observedApiCalls++;
		if (!setStatus.IsOk || state.Top != 1 || !state.IsTable(1))
		{
			return Fail(iteration, "TrySetIndex", "did not consume the value and preserve the table");
		}

		LuaStatus getStatus = state.TryGetIndex(1, key);
		observedApiCalls++;
		if (!getStatus.IsOk || state.Top != 2 || !state.TryReadInteger(2, out long actual) || actual != value)
		{
			return Fail(iteration, "TryGetIndex", "did not return the value written through the protected setter");
		}

		state.Pop(2);
		return AssertEmpty(state, iteration, "table-mutation-cleanup");
	}

	private static int RejectInvalidTable(LuaState state, long key, int iteration, ref int observedApiCalls)
	{
		state.PushInteger(Sentinel);
		LuaStatus status = state.TryGetIndex(1, key);
		observedApiCalls++;
		if (status != LuaStatus.RuntimeError || state.Top != 2 || !state.TryReadInteger(1, out long sentinel) ||
			sentinel != Sentinel || !state.TryReadString(2, out string? error) || string.IsNullOrWhiteSpace(error))
		{
			return Fail(iteration, "TryGetIndex-invalid-table", "did not return one runtime error above the unchanged sentinel");
		}

		state.Pop(2);
		return AssertEmpty(state, iteration, "invalid-table-cleanup");
	}

	private static int RejectAllocation(LuaState state, int capacity, int iteration, ref int observedApiCalls)
	{
		state.PushInteger(Sentinel);
		LuaStatus status = Program.CaptureMemoryException(() => state.CreateTable(capacity, capacity));
		observedApiCalls++;
		if (status != LuaStatus.MemoryError || state.Top != 1 || !state.TryReadInteger(1, out long sentinel) ||
			sentinel != Sentinel)
		{
			return Fail(iteration, "CreateTable-allocator-refusal", "did not return memory error while restoring the sentinel");
		}

		state.Pop(1);
		return AssertEmpty(state, iteration, "allocator-refusal-cleanup");
	}

	private static int Recover(LuaState state, int iteration, ref int observedApiCalls)
	{
		LuaStatus status = state.TryExecute("return 6 * 7"u8, 1, "=protected-fuzz-recovery"u8);
		observedApiCalls++;
		if (!status.IsOk || state.Top != 1 || !state.TryReadInteger(1, out long value) || value != 42)
		{
			return Fail(iteration, "recovery", "the state did not execute the fixed protected recovery call");
		}

		state.Pop(1);
		return 0;
	}

	private static int AssertEmpty(LuaState state, int iteration, string operation)
	{
		return state.Top == 0 ? 0 : Fail(iteration, operation, "did not completely clean up the Lua stack");
	}

	private static int Fail(int iteration, string operation, string detail)
	{
		Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"FAIL protected fuzz seed=0x{Seed:X8} iteration={iteration} operation={operation}: {detail}"));
		return 1;
	}

	private struct RandomSource(uint state)
	{
		private uint _state = state;

		public int NextInt32(int exclusiveMaximum)
		{
			return (int) (NextUInt32() % (uint) exclusiveMaximum);
		}

		public long NextInt64()
		{
			return unchecked(((long) NextUInt32() << 32) | NextUInt32());
		}

		public byte[] NextBytes(int length)
		{
			byte[] result = new byte[length];
			for (int i = 0; i < result.Length; i++)
			{
				result[i] = (byte) NextUInt32();
			}

			return result;
		}

		private uint NextUInt32()
		{
			uint value = _state;
			value ^= value << 13;
			value ^= value >> 17;
			value ^= value << 5;
			_state = value;
			return value;
		}
	}
}
