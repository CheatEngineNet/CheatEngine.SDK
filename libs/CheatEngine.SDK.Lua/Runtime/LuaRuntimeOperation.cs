using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     An admitted, stack-bound Lua operation: its <see cref="State" /> remains usable until <see cref="Dispose" />
///     returns, while a host lifecycle transition waits for it to finish.
/// </summary>
/// <remarks>
///     Acquire this value with <see cref="LuaRuntime.AcquireOperation()" /> and use the pattern
///     <c>using var operation = LuaRuntime.AcquireOperation(); var state = operation.State;</c>. The operation begins
///     before the host state provider runs and ends only when disposed, so it covers every stack operation and resource
///     publication in the body. It is a <c>ref struct</c>: do not copy, store, box, capture or await across it. Lua work
///     is synchronous and thread-affine; dispose on the acquiring thread before returning to the host.
/// </remarks>
public ref struct LuaRuntimeOperation
{
    private bool _admitted;

    internal LuaRuntimeOperation(LuaState state, bool admitted)
    {
        State = state;
        _admitted = admitted;
    }

    /// <summary>Gets the Lua state acquired for this operation on the calling thread.</summary>
    public LuaState State { get; }

    /// <summary>
    ///     Ends the operation admission. Idempotent for the same value; callers should rely on a <see langword="using" /> scope
    ///     instead of invoking it directly.
    /// </summary>
    public void Dispose()
    {
        if (!_admitted) return;

        _admitted = false;
        LuaRuntime.ExitOperation();
    }
}
