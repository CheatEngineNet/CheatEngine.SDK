using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.CompilerServices;

/// <summary>
///     The generator-facing helpers behind optional arguments, optional results and variadic results
///     (<see cref="LuaCallSupport.PushOptional{T,TMarshaller}" />,
///     <see cref="LuaCallSupport.TryReadOptional{T,TMarshaller}" />,
///     <see cref="LuaCallSupport.ReadResults{T,TMarshaller}" />): omitted, <c>nil</c> and a value stay distinct, reads
///     never
///     change the stack, and a failure is classified by type, never by text.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class LuaCallSupportOptionalTests
{
	[Fact]
	public void PushOptional_pushes_a_value_or_nil_and_refuses_an_omitted_argument()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		LuaCallSupport.PushOptional<long, Int64Marshaller>(L, LuaOptional.Of(42L));
		LuaCallSupport.PushOptional<string, StringMarshaller>(L, LuaOptional.Nil<string>());
		ArgumentException omitted = Assert.Throws<ArgumentException>(() =>
			LuaCallSupport.PushOptional<long, Int64Marshaller>(L, default));

		Assert.Equal("value", omitted.ParamName);
		Assert.Equal(2, L.Top);
		Assert.True(L.IsInteger(1));
		Assert.True(L.IsNil(2));
	}

	[Fact]
	public void TryReadOptional_reads_absent_as_omitted_nil_as_nil_and_refuses_another_kind()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushInteger(7);
		L.PushNil();
		L.PushString("text"u8);

		Assert.True(LuaCallSupport.TryReadOptional<long, Int64Marshaller>(L, 1, out LuaOptional<long> value));
		Assert.Equal(LuaOptional.Of(7L), value);
		Assert.True(LuaCallSupport.TryReadOptional<long, Int64Marshaller>(L, 2, out LuaOptional<long> nil));
		Assert.True(nil.IsNil);
		Assert.True(LuaCallSupport.TryReadOptional<long, Int64Marshaller>(L, 4, out LuaOptional<long> absent));
		Assert.True(absent.IsOmitted);
		Assert.False(LuaCallSupport.TryReadOptional<long, Int64Marshaller>(L, 3, out LuaOptional<long> wrong));
		Assert.True(wrong.IsOmitted);
		Assert.True(LuaCallSupport.TryReadOptional<string, StringMarshaller>(L, 3, out LuaOptional<string> text));
		Assert.Equal(LuaOptional.Of("text"), text);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			LuaCallSupport.TryReadOptional<long, Int64Marshaller>(L, -1, out _));
		Assert.Equal(3, L.Top);
		Assert.Equal(LuaType.String, L.TypeOf(3));
	}

	[Fact]
	public void ReadResults_copies_every_value_to_the_top_or_reports_the_needed_capacity()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushString("before"u8);
		L.PushInteger(10);
		L.PushInteger(20);
		L.PushInteger(30);
		Span<long> values = stackalloc long[4];
		Span<long> small = stackalloc long[2];

		Assert.Equal(LuaOperationStatus.Success,
			LuaCallSupport.ReadResults<long, Int64Marshaller>(L, 2, values, out int count));
		Assert.Equal(3, count);
		Assert.Equal([10L, 20L, 30L], values[..3].ToArray());
		Assert.Equal(LuaOperationStatus.ResultCapacityExceeded,
			LuaCallSupport.ReadResults<long, Int64Marshaller>(L, 2, small, out int needed));
		Assert.Equal(3, needed);
		Assert.Equal([0L, 0L], small.ToArray());
		Assert.Equal(LuaOperationStatus.Success,
			LuaCallSupport.ReadResults<long, Int64Marshaller>(L, 5, small, out int none));
		Assert.Equal(0, none);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			LuaCallSupport.ReadResults<long, Int64Marshaller>(L, 0, new long[1], out _));
		Assert.Equal(4, L.Top);
	}

	[Fact]
	public void ReadResults_classifies_a_nil_or_wrong_kind_value_and_clears_what_it_copied()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushInteger(1);
		L.PushInteger(2);
		L.PushNil();
		Span<long> values = stackalloc long[4];

		Assert.Equal(LuaOperationStatus.NilResult,
			LuaCallSupport.ReadResults<long, Int64Marshaller>(L, 1, values, out int nilCount));
		Assert.Equal(0, nilCount);
		Assert.Equal([0L, 0L, 0L, 0L], values.ToArray());

		L.SetTop(2);
		L.PushBoolean(true);
		Assert.Equal(LuaOperationStatus.InvalidResult,
			LuaCallSupport.ReadResults<long, Int64Marshaller>(L, 1, values, out int invalidCount));
		Assert.Equal(0, invalidCount);
		Assert.Equal([0L, 0L, 0L, 0L], values.ToArray());
		Assert.Equal(3, L.Top);
	}

	[Fact]
	public void Optional_helpers_allocate_nothing_when_warm()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		long[] buffer = new long[4];
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			LuaCallSupport.PushOptional<long, Int64Marshaller>(L, LuaOptional.Of(5L));
			LuaCallSupport.PushOptional<long, Int64Marshaller>(L, LuaOptional.Of(6L));
			if (!LuaCallSupport.TryReadOptional<long, Int64Marshaller>(L, 1, out LuaOptional<long> value)
				|| !LuaCallSupport.ReadResults<long, Int64Marshaller>(L, 1, buffer, out int count).IsSuccess
				|| count != 2)
			{
				throw new InvalidOperationException("wrong read");
			}

			LuaCallSupport.PushOptional<double, DoubleMarshaller>(L, LuaOptional.Nil<double>());
			sink += value.Value + buffer[1];
			L.SetTop(0);
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}
}
