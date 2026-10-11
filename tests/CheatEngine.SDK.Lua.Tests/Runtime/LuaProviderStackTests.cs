using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>Acquiring a provider state must preserve values the caller already owns.</summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class LuaProviderStackTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Acquire_preserves_existing_stack_values_for_first_stamp_and_verification(bool deferredStamp)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(openLibraries: false);
		LuaRuntime.Detach();
		LuaState view = new(handle: state.Pointer);
		view.PushInteger(123);
		view.PushBoolean(true);
		LuaHostBinding binding = HostDouble.CreateBinding(deferredStamp ? null : state.L);
		LuaRuntime.Attach(binding);
		HostDouble.SetStateForCurrentThread(state.Pointer);
		try
		{
			for (int index = 0; index < 3; index++)
			{
				using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
				Assert.Equal(state.Pointer, operation.State.Handle);
				Assert.Equal(2, operation.State.Top);
				Assert.True(operation.State.TryReadInteger(1, out long value));
				Assert.Equal(123, value);
				Assert.True(operation.State.ToBoolean(2));
			}

			// A transition drains admissions only after all three operation leases have been released.
			using (LuaRuntime.BeginStateReset())
			{
			}
			Assert.Equal(2, LuaApi.lua_gettop(state.L));
		}
		finally
		{
			LuaRuntime.Detach();
		}
	}
}
