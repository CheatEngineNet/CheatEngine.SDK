using CheatEngine.SDK.Lua.Callbacks;

namespace CheatEngine.SDK.Lua.Tests.Callbacks;

/// <summary>The managed state a <see cref="LuaCallback{TState}" /> carries in the callback tests.</summary>
internal sealed class Counter
{
    public int Value { get; set; }

    public nint LastState { get; set; }
}
