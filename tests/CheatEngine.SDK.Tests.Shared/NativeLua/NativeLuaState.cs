using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Tests.Shared.NativeLua;

/// <summary>
///     An independent Lua state for one test or one benchmark: created in the constructor, closed by
///     <see cref="Dispose" />. Not thread-safe: use it from the thread that created it. No finalizer on purpose (a Lua
///     state
///     must not be closed from the finalizer thread); a state that is never disposed leaks until the process ends.
/// </summary>
internal sealed unsafe class NativeLuaState : IDisposable
{
    private lua_State* _state;

    /// <summary>Creates a state with the library's default allocator.</summary>
    /// <param name="openLibraries">True to open every standard library (<c>luaL_openlibs</c>); false for a bare state.</param>
    /// <exception cref="InvalidOperationException">
    ///     No Lua library is available (check
    ///     <see cref="NativeLuaLibrary.IsAvailable" /> first), or the state could not be allocated.
    /// </exception>
    public NativeLuaState(bool openLibraries = true)
    {
        NativeLuaLibrary.ThrowIfUnavailable();

        _state = LuaApi.luaL_newstate();
        if (_state is null)
            throw new InvalidOperationException(
                "luaL_newstate returned null: the Lua library could not allocate a state.");

        if (openLibraries) LuaApi.luaL_openlibs(_state);
    }

    /// <summary>The raw state. Borrowed: never close it yourself.</summary>
    /// <exception cref="ObjectDisposedException">The state is closed.</exception>
    public lua_State* L
    {
        get
        {
            ObjectDisposedException.ThrowIf(_state is null, this);
            return _state;
        }
    }

    /// <summary>The raw state as an integer, for code that does not want a pointer type in its signatures.</summary>
    /// <exception cref="ObjectDisposedException">The state is closed.</exception>
    public nint Pointer => (nint)L;

    /// <summary>Closes the state; every pointer obtained from it dangles afterwards. Idempotent.</summary>
    public void Dispose()
    {
        if (_state is null) return;
        LuaApi.lua_close(_state);
        _state = null;
    }
}
