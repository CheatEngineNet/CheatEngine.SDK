using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CESDK.Lua.State;

/// <summary>
///     Stack-balance guard for hand-written code: records the stack top of a <see cref="LuaState" /> when created and
///     restores it when disposed, on every path (normal return, early return, exception).
/// </summary>
/// <remarks>
///     <para>
///         <c>using LuaFrame frame = new(L);</c> at the start of an operation is the whole protocol. Straight-line
///         generated
///         bodies use an explicit <see cref="LuaState.Top" /> / <see cref="LuaState.SetTop" /> pair instead, to stay free
///         of
///         an exception-handling region on the success path; both forms uphold the same invariant.
///     </para>
///     <para>
///         A frame never raises: <c>lua_settop</c> cannot fail, and disposing twice is harmless. It owns nothing but the
///         number it recorded, so copies are safe but pointless. A frame is bound to the thread and state it was created
///         on.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct LuaFrame : IDisposable
{
    private readonly LuaState _state;

    /// <summary>Records the current top of <paramref name="state" />.</summary>
    /// <param name="state">The state to guard; must not be <see cref="LuaState.IsNull" />.</param>
    public LuaFrame(LuaState state)
    {
        _state = state;
        Top = state.Top;
    }

    /// <summary>Gets the guarded state.</summary>
    public LuaState State => _state;

    /// <summary>
    ///     Gets the stack top recorded at creation: the height <see cref="Dispose" /> restores, and the index below the
    ///     frame's first own value.
    /// </summary>
    public int Top { get; }

    /// <summary>Gets the number of values the frame currently owns: those pushed since it was created.</summary>
    public int Count => _state.Top - Top;

    /// <summary>
    ///     Asserts, in Debug builds only, that the stack is exactly at the recorded height: for code that claims to be
    ///     balanced by construction before the frame restores anything.
    /// </summary>
    [Conditional("DEBUG")]
    public void AssertBalanced()
    {
        Debug.Assert(_state.Top == Top, "The Lua stack is not at the height recorded by the frame.");
    }

    /// <summary>Restores the recorded top, dropping every value pushed inside the frame. Never raises; idempotent.</summary>
    public void Dispose()
    {
        // A frame that ends below its own start has consumed values it did not own: restoring would paper over it
        // with nils. Only a Debug build can tell; Release restores regardless, which is the safer of two wrongs.
        Debug.Assert(_state.Top >= Top,
            "The Lua stack is below the height recorded by the frame: values that were not pushed inside it have been popped.");
        _state.SetTop(Top);
    }
}
