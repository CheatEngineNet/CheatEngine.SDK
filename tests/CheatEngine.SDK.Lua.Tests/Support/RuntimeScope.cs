using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Support;

/// <summary>
///     Attaches <see cref="LuaRuntime" /> to a fixture state through <see cref="HostDouble" /> for the duration of a test
///     and detaches on dispose, so that no test leaves the process-wide runtime attached.
/// </summary>
internal sealed unsafe class RuntimeScope : IDisposable
{
    public RuntimeScope(NativeLuaState state, bool withPusher = true)
    {
        Binding = HostDouble.CreateBinding(state.L, withPusher);
        LuaRuntime.Attach(Binding);
    }

    public LuaHostBinding Binding { get; }

    public void Dispose()
    {
        LuaRuntime.Detach();
    }
}
