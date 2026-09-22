using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.State;

/// <summary>The stack guard restores the top on every path.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaFrameTests
{
	[Fact]
	public void Restores_top_on_dispose()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushInteger(1);

		using (LuaFrame frame = new(L))
		{
			Assert.Equal(1, frame.Top);
			Assert.Equal(L, frame.State);
			L.PushInteger(2);
			L.PushInteger(3);
			L.PushNil();
			Assert.Equal(3, frame.Count);
			Assert.Equal(4, L.Top);
		}

		Assert.Equal(1, L.Top);
		Assert.True(L.TryReadInteger(1, out long kept));
		Assert.Equal(1, kept);
	}

	[Fact]
	public void Restores_top_on_early_return()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		Assert.False(ReturnsEarly(L));

		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Restores_top_when_an_exception_passes_through()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		Assert.Throws<InvalidOperationException>(() => Throws(L));

		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Restores_top_after_a_failed_protected_call_left_an_error_value()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);

		using (LuaFrame frame = new(L))
		{
			LuaStatus status = L.TryExecute("error('boom')"u8, 0);
			Assert.Equal(LuaStatus.RuntimeError, status);
			Assert.Equal(1, frame.Count);
			Assert.Contains("boom", LuaError.FromStack(L, status).Message, StringComparison.Ordinal);
		}

		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Disposing_twice_is_harmless_and_a_balanced_frame_asserts_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaFrame frame = new(L);
		L.PushInteger(1);
		L.Pop(1);
		frame.AssertBalanced();

		frame.Dispose();
		frame.Dispose();

		Assert.Equal(0, L.Top);
	}

	private static bool ReturnsEarly(LuaState L)
	{
		using LuaFrame frame = new(L);
		L.PushInteger(42);
		L.PushString("x"u8);
		if (L.Top > 0)
		{
			return false;
		}

		return true;
	}

	private static void Throws(LuaState L)
	{
		using LuaFrame frame = new(L);
		L.PushInteger(42);
		throw new InvalidOperationException("managed failure inside a frame");
	}
}
