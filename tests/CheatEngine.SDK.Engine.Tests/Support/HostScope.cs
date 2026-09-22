using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Support;

/// <summary>
///     Attaches <see cref="LuaRuntime" /> to a fixture state through <see cref="FakeHost" /> for the duration of a test
///     and detaches on dispose, so that no test leaves the process-wide runtime attached.
/// </summary>
internal sealed class HostScope : IDisposable
{
	public HostScope(NativeLuaState state, bool withPusher = true)
	{
		State = EngineTest.View(state);
		Binding = FakeHost.CreateBinding(state, withPusher);
		LuaRuntime.Attach(Binding);
	}

	/// <summary>The fixture state as the SDK sees it.</summary>
	public LuaState State
	{
		get;
	}

	public LuaHostBinding Binding
	{
		get;
	}

	public void Dispose()
	{
		LuaRuntime.Detach();
	}
}
