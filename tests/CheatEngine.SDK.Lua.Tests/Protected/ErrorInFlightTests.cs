using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Callbacks;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Protected;

/// <summary>
///     Audit A05-17: a managed callback that fails while an error is already in flight. The message handler of a
///     protected call is a managed thunk whose body throws; the thunk reports the failure, its wrapper raises, and Lua
///     handles the handler's own error as an error in error handling. The caller gets one distinct status, the stack
///     is exactly as documented, and the state keeps working.
/// </summary>
/// <remarks>
///     Every managed frame has returned before Lua raises: the thunk only reports the failure, and the SDK wrapper,
///     which is Lua code, raises it. No <c>longjmp</c> crosses a managed frame, however deep the handler recursion.
/// </remarks>
[Trait("Category", "NativeLua")]
public sealed class ErrorInFlightTests
{
	[Fact]
	public void Managed_callback_failing_inside_a_message_handler_yields_one_distinct_status_and_restores_the_stack()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		L.PushInteger(7);
		Assert.True(L.TryPushFunction(Thunks.Throw).IsOk);
		int handler = L.Top;
		Assert.True(L.TryLoad("error('inner')"u8, "=t"u8).IsOk);

		LuaStatus status = L.TryCall(0, 0, handler);

		Assert.Equal(LuaStatus.MessageHandlerError, status);
		Assert.Equal(handler + 1, L.Top);
		Assert.Equal("error in error handling", LuaError.FromStack(L, status).Message);
		Assert.True(L.TryReadInteger(1, out long below));
		Assert.Equal(7, below);

		L.SetTop(0);
		LuaStatus next = L.TryExecute("return 6 * 7"u8, 1);
		Assert.True(next.IsOk);
		Assert.True(L.TryReadInteger(-1, out long value));
		Assert.Equal(42, value);
		Assert.Equal(1, L.Top);
	}
}
