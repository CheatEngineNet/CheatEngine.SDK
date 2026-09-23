using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Memory;

/// <summary>
///     Qualification Q21 and Q22 at C2 through the Engine's own generated wrappers (<see cref="MemoryScalars" />, emitted
///     from the <c>memory-scalars</c> spec): a signed 32-bit read keeps -1, an address above 4 GiB or above
///     <see cref="long.MaxValue" /> reaches Lua as the exact integer, and the Try form tells a value from <c>nil</c>,
///     <c>false</c> and a raise (audit A12-22, A12-23). The stand-ins record what Lua received.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryScalarsFidelityTests
{
	private static ReadOnlySpan<byte> StandIn => """
	                                             seen = {}
	                                             local function record(prefix, a) seen[#seen + 1] = prefix .. math.type(a) .. ':' .. string.format('%x', a) end
	                                             function readInteger(a, signed)
	                                               record('r32|', a)
	                                               if signed == true then return -1 end
	                                               return 4294967295
	                                             end
	                                             function readQword(a)
	                                               record('r64|', a)
	                                               if a == 1 then return nil end
	                                               if a == 2 then return false end
	                                               if a == 3 then error('access violation') end
	                                               if a == 4 then return 0 end
	                                               if a == 5 then return end
	                                               return math.maxinteger
	                                             end
	                                             function writeQword(a, v) record('w64|', a) seen[#seen + 1] = math.type(v) .. ':' .. tostring(v) return true end
	                                             """u8;

	[Fact]
	[Trait("Qualification", "Q21")]
	public void Reads_int32_minus_one_without_a_double_conversion()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);

		// The generated wrapper passes signed = true: the stand-in answers -1 only then, 4294967295 otherwise.
		Assert.True(MemoryScalars.TryReadInt32(new Address(0x10), out int value));
		Assert.Equal(-1, value);
		Assert.Equal("r32|integer:10", Seen(scope, 1));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void Address_above_4_gib_reaches_lua_as_an_exact_integer()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);

		Assert.True(MemoryScalars.TryReadInt64(new Address(0x1_0000_0000UL), out long maximum));
		Assert.Equal(long.MaxValue, maximum);
		Assert.Equal("r64|integer:100000000", Seen(scope, 1));
		Assert.True(MemoryScalars.WriteInt64(new Address(0xFFFF_FFFF_FFFF_F000UL), long.MinValue));
		Assert.Equal("w64|integer:fffffffffffff000", Seen(scope, 2));
		Assert.Equal("integer:-9223372036854775808", Seen(scope, 3));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Try_read_reports_nil_false_and_raise_as_failure_and_zero_as_value()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, StandIn);

		Assert.False(MemoryScalars.TryReadInt64(new Address(1), out long nil));
		Assert.Equal(0, nil);
		Assert.False(MemoryScalars.TryReadInt64(new Address(2), out long no));
		Assert.Equal(0, no);
		Assert.False(MemoryScalars.TryReadInt64(new Address(3), out long raised));
		Assert.Equal(0, raised);
		Assert.Equal(0, scope.State.Top);
		Assert.False(MemoryScalars.TryReadInt64(new Address(5), out long none));
		Assert.Equal(0, none);
		Assert.True(MemoryScalars.TryReadInt64(new Address(4), out long zero));
		Assert.Equal(0, zero);
		Assert.Equal(0, scope.State.Top);
	}

	private static string Seen(HostScope scope, int index)
	{
		EngineTest.Run(scope.State,
			Encoding.UTF8.GetBytes("return seen[" + index.ToString(CultureInfo.InvariantCulture) + "]"), 1);
		string value = EngineTest.ReadString(scope.State, -1);
		scope.State.Pop(1);
		return value;
	}
}
