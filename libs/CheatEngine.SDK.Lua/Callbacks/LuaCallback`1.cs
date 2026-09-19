using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.References;

namespace CheatEngine.SDK.Lua.Callbacks;

/// <summary>
///     A <see cref="LuaCallback" /> whose managed state is known to be a <typeparamref name="TState" />. Created by
///     <see cref="LuaCallback.TryCreate{TState}" />.
/// </summary>
/// <typeparam name="TState">The state's type, a class.</typeparam>
public sealed class LuaCallback<TState> : LuaCallback
    where TState : class
{
    internal LuaCallback(GCHandle<object> handle, LuaRef closure, LuaRef wrapped)
        : base(handle, closure, wrapped)
    {
    }

    /// <summary>Gets the managed state object; <see langword="null" /> after release.</summary>
    public TState? State => StateObject as TState;
}
