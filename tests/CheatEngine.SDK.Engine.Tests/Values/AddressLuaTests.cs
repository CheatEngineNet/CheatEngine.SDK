using System.Globalization;

using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Values;

/// <summary>The number-or-hex-string convention on a real Lua stack, and the address as its own marshaller.</summary>
[Trait("Category", "NativeLua")]
public sealed class AddressLuaTests
{
	[Fact]
	public void A_number_is_read_by_bit_reinterpretation()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);

		L.PushInteger(0x400000);
		L.PushInteger(-1);
		L.PushNumber(4198400.0);
		L.PushNumber(2.5);

		Assert.True(Address.TryRead(L, 1, out Address low));
		Assert.Equal(0x400000UL, low.Value);
		Assert.True(Address.TryRead(L, 2, out Address high));
		Assert.Equal(ulong.MaxValue, high.Value);
		Assert.True(Address.TryRead(L, 3, out Address integralFloat));
		Assert.Equal(0x401000UL, integralFloat.Value);
		Assert.False(Address.TryRead(L, 4, out Address fraction));
		Assert.Equal(Address.Zero, fraction);
		Assert.Equal(4, L.Top);
	}

	[Fact]
	public void A_string_is_read_as_hexadecimal_never_as_decimal()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);

		L.PushString("00400000"u8);
		L.PushString("0x7FF6A1B2C3D4"u8);
		L.PushString("10"u8);
		L.PushString("kernel32.dll+10"u8);
		L.PushString(""u8);

		Assert.True(Address.TryRead(L, 1, out Address padded));
		Assert.Equal(0x400000UL, padded.Value);
		Assert.True(Address.TryRead(L, 2, out Address prefixed));
		Assert.Equal(0x7FF6A1B2C3D4UL, prefixed.Value);
		Assert.True(Address.TryRead(L, 3, out Address ten));
		Assert.Equal(0x10UL, ten.Value);
		Assert.False(Address.TryRead(L, 4, out _));
		Assert.False(Address.TryRead(L, 5, out _));

		// The strings were not converted in place.
		Assert.Equal(LuaType.String, L.TypeOf(1));
		Assert.Equal(LuaType.String, L.TypeOf(3));
		Assert.Equal(5, L.Top);
	}

	[Fact]
	public void A_string_that_is_not_hexadecimal_never_falls_back_to_Lua_number_coercion()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);

		// Each of these is a number to lua_tointegerx (10, 16 and 10); to this type they are text and not addresses.
		// ("1e1" would not do: it is the hexadecimal 0x1E1.)
		L.PushString("1e+1"u8);
		L.PushString("0x1p4"u8);
		L.PushString("10.0"u8);

		for (int index = 1; index <= 3; index++)
		{
			Assert.True(L.TryReadInteger(index, out _),
				"index " + index.ToString(CultureInfo.InvariantCulture) + " should be convertible by Lua");
			Assert.False(Address.TryRead(L, index, out Address address),
				"index " + index.ToString(CultureInfo.InvariantCulture) + " was read as an address");
			Assert.Equal(Address.Zero, address);
			Assert.Equal(LuaType.String, L.TypeOf(index));
		}
	}

	[Fact]
	public void Other_types_and_absent_values_are_not_addresses()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);

		L.PushNil();
		L.PushBoolean(true);
		L.CreateTable();
		L.PushLightUserdata(0x400000);

		for (int index = 1; index <= 5; index++)
		{
			Assert.False(Address.TryRead(L, index, out Address address));
			Assert.Equal(Address.Zero, address);
		}
	}

	[Fact]
	public void Push_and_read_round_trip_through_the_marshaller_contract()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);

		Address high = 0xFFFF_FFFF_FFFF_FFF0;
		Address.Push(L, high);
		Assert.True(L.IsInteger(-1));
		Assert.True(L.TryReadInteger(-1, out long bits));
		Assert.Equal(unchecked((long) 0xFFFF_FFFF_FFFF_FFF0UL), bits);
		Assert.True(RoundTrip<Address, Address>(L, out Address back));
		Assert.Equal(high, back);
		Assert.Equal(1, L.Top);
	}

	[Fact]
	public void Reading_either_form_allocates_nothing()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);
		L.PushInteger(0x400000);
		L.PushString("0x7FF6A1B2C3D4"u8);
		ulong sink = 0;

		AllocationGate.AssertZero(() =>
		{
			if (!Address.TryRead(L, 1, out Address number))
			{
				Assert.Fail("number read failed");
			}

			if (!Address.TryRead(L, 2, out Address text))
			{
				Assert.Fail("text read failed");
			}

			sink += number.Value + text.Value;
			Address.Push(L, number);
			L.Pop(1);
		});

		Assert.NotEqual(0UL, sink);
	}

	private static bool RoundTrip<TMarshaller, T>(LuaState L, out T value)
		where TMarshaller : struct, ILuaMarshaller<T>
	{
		return TMarshaller.TryRead(L, -1, out value!);
	}
}
