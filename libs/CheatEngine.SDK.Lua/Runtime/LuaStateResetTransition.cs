namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     Holds the SDK's exclusive side of a host-owned Lua-state replacement until the host has completed its reset.
/// </summary>
/// <remarks>
///     Created only by <see cref="LuaRuntime.BeginStateReset" />. The host must reset the state before disposing this
///     value on the same thread. It is stack-only to prevent a reset transition from escaping its lifecycle boundary.
/// </remarks>
internal ref struct LuaStateResetTransition
{
    private bool _active;

    internal LuaStateResetTransition(bool active)
    {
        _active = active;
    }

    public void Dispose()
    {
        if (!_active) return;

        _active = false;
        LuaRuntime.CompleteStateReset();
    }
}
