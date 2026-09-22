using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Enums;

/// <summary>Enums as the Lua integers Cheat Engine exchanges them as.</summary>
public sealed class EnumMarshallerTests
{
	[Fact]
	public void ToInt64_carries_the_value_of_every_underlying_type()
	{
		Assert.Equal(2L, EnumMarshaller<VariableType>.ToInt64(VariableType.Dword));
		Assert.Equal(128L, EnumMarshaller<MemoryProtection>.ToInt64(MemoryProtection.ExecuteWriteCopy));
		Assert.Equal(250L, EnumMarshaller<Narrow>.ToInt64(Narrow.High));
		Assert.Equal(-5L, EnumMarshaller<Signed>.ToInt64(Signed.Negative));
		Assert.Equal(-1L, EnumMarshaller<Wide>.ToInt64(Wide.Top));
	}

	[Fact]
	public void TryFromInt64_accepts_what_fits_and_refuses_the_rest()
	{
		Assert.True(EnumMarshaller<VariableType>.TryFromInt64(7, out VariableType wide));
		Assert.Equal(VariableType.WideString, wide);
		Assert.True(EnumMarshaller<VariableType>.TryFromInt64(99, out VariableType undefined));
		Assert.Equal((VariableType) 99, undefined);
		Assert.False(EnumMarshaller<VariableType>.TryFromInt64(long.MaxValue, out VariableType overflow));
		Assert.Equal(default, overflow);

		Assert.True(EnumMarshaller<MemoryProtection>.TryFromInt64(20, out MemoryProtection combined));
		Assert.Equal(MemoryProtection.ReadWrite | MemoryProtection.Execute, combined);
		Assert.False(EnumMarshaller<MemoryProtection>.TryFromInt64(-1, out _));

		Assert.True(EnumMarshaller<Narrow>.TryFromInt64(250, out Narrow high));
		Assert.Equal(Narrow.High, high);
		Assert.False(EnumMarshaller<Narrow>.TryFromInt64(256, out _));
		Assert.True(EnumMarshaller<Signed>.TryFromInt64(-5, out Signed negative));
		Assert.Equal(Signed.Negative, negative);
		Assert.True(EnumMarshaller<Wide>.TryFromInt64(-1, out Wide top));
		Assert.Equal(Wide.Top, top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Push_and_read_round_trip_on_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);

		EnumMarshaller<ScanOption>.Push(L, ScanOption.Unchanged);
		EnumMarshaller<MemoryProtection>.Push(L, MemoryProtection.ExecuteReadWrite);
		L.PushNumber(2.0);
		L.PushString("2"u8);
		L.PushNil();

		Assert.True(L.IsInteger(1));
		Assert.Equal(10, EngineTest.ReadInteger(L, 1));
		Assert.True(EnumMarshaller<ScanOption>.TryRead(L, 1, out ScanOption option));
		Assert.Equal(ScanOption.Unchanged, option);
		Assert.True(EnumMarshaller<MemoryProtection>.TryRead(L, 2, out MemoryProtection protection));
		Assert.Equal(MemoryProtection.ExecuteReadWrite, protection);
		Assert.True(EnumMarshaller<RoundingType>.TryRead(L, 3, out RoundingType fromFloat));
		Assert.Equal(RoundingType.Truncated, fromFloat);
		Assert.True(EnumMarshaller<VariableType>.TryRead(L, 4, out VariableType fromString));
		Assert.Equal(VariableType.Dword, fromString);
		Assert.False(EnumMarshaller<VariableType>.TryRead(L, 5, out VariableType fromNil));
		Assert.Equal(default, fromNil);
		Assert.Equal(5, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Push_and_read_allocate_nothing()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		int sink = 0;

		AllocationGate.AssertZero(() =>
		{
			EnumMarshaller<VariableType>.Push(L, VariableType.Qword);
			if (!EnumMarshaller<VariableType>.TryRead(L, -1, out VariableType back))
			{
				Assert.Fail("read failed");
			}

			sink += (int) back;
			L.Pop(1);
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	private enum Narrow : byte
	{
		Low = 1,
		High = 250
	}

	private enum Signed : short
	{
		Negative = -5
	}

	private enum Wide : ulong
	{
		Top = ulong.MaxValue
	}
}
