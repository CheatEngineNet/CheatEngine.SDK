using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     A stand-in for the host's state provider, in the shape <see cref="LuaHostBinding" /> expects (a <c>stdcall</c>
///     <c>void*</c>-returning function): it returns the fixture state a test installed, so that generated wrapper
///     bodies calling <c>LuaRuntime.AcquireState()</c> reach that state.
/// </summary>
internal static unsafe class HostDouble
{
	private static lua_State* s_state;

	/// <summary>
	///     Points the provider at <paramref name="state" /> and builds a binding with the calling thread as main thread
	///     and no object pusher.
	/// </summary>
	public static LuaHostBinding CreateBinding(lua_State* state)
	{
		s_state = state;
		delegate* unmanaged[Stdcall]<void*> provider = &Provide;
		return new LuaHostBinding((nint) provider, 0, Environment.CurrentManagedThreadId);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void* Provide()
	{
		return s_state;
	}
}
