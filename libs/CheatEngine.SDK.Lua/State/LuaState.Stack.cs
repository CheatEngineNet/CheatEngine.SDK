using System.Diagnostics;
using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

// Stack manipulation: one C API call each, none of them can raise or run Lua code.
public readonly unsafe partial struct LuaState
{
    /// <summary>Gets the index of the top element, which is also the number of elements on the stack (<c>lua_gettop</c>).</summary>
    public int Top
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => lua_gettop(Pointer);
    }

    /// <summary>
    ///     Sets the stack height (<c>lua_settop</c>): drops everything above <paramref name="index" />, or fills with
    ///     <c>nil</c> when it grows. This is the restore half of the stack-balance invariant.
    /// </summary>
    /// <param name="index">The new top: a value previously read from <see cref="Top" />, or a relative index.</param>
    /// <remarks>Never raises. Growing beyond the space guaranteed by <see cref="TryEnsureStack" /> is undefined behaviour.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTop(int index)
    {
        lua_settop(Pointer, index);
    }

    /// <summary>Drops the top <paramref name="count" /> elements (<c>lua_pop</c>).</summary>
    /// <param name="count">Number of elements, at most <see cref="Top" />.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pop(int count)
    {
        Debug.Assert(count >= 0, "A negative count would grow the stack.");
        lua_settop(Pointer, -count - 1);
    }

    /// <summary>Pushes a copy of the value at <paramref name="index" /> (<c>lua_pushvalue</c>).</summary>
    /// <param name="index">A valid index.</param>
    [LuaStackEffect(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushValue(int index)
    {
        lua_pushvalue(Pointer, index);
    }

    /// <summary>
    ///     Converts a relative index into an absolute one that stays correct while values are pushed (<c>lua_absindex</c>
    ///     ).
    /// </summary>
    /// <param name="index">An acceptable index.</param>
    /// <returns>The equivalent positive index; pseudo-indices are returned unchanged.</returns>
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AbsoluteIndex(int index)
    {
        return lua_absindex(Pointer, index);
    }

    /// <summary>
    ///     Makes sure <paramref name="extraSlots" /> more values can be pushed (<c>lua_checkstack</c>).
    /// </summary>
    /// <param name="extraSlots">Number of additional slots needed.</param>
    /// <returns><see langword="false" /> when the stack cannot grow that far; nothing changed.</returns>
    /// <remarks>
    ///     A C function starts with <see cref="MinimumFreeSlots" /> free slots. Every protected member of this type needs
    ///     at most four of them, except the first protected operation on a given Lua state, which installs the helper
    ///     chunk and checks for eleven itself (it grows the stack if it must). Call this before a body pushes more than
    ///     about sixteen values. Never raises.
    /// </remarks>
    [LuaStackEffect(0)]
    public bool TryEnsureStack(int extraSlots)
    {
        return lua_checkstack(Pointer, extraSlots) != 0;
    }

    /// <summary>Moves the top element into <paramref name="index" />, shifting the elements above it up (<c>lua_insert</c>).</summary>
    /// <param name="index">A valid index that is not a pseudo-index.</param>
    [LuaStackEffect(0)]
    public void Insert(int index)
    {
        lua_rotate(Pointer, index, 1);
    }

    /// <summary>Removes the element at <paramref name="index" />, shifting the elements above it down (<c>lua_remove</c>).</summary>
    /// <param name="index">A valid index that is not a pseudo-index.</param>
    [LuaStackEffect(-1)]
    public void Remove(int index)
    {
        lua_rotate(Pointer, index, -1);
        lua_settop(Pointer, -2);
    }

    /// <summary>Pops the top element into <paramref name="index" />, overwriting what was there (<c>lua_replace</c>).</summary>
    /// <param name="index">A valid index.</param>
    [LuaStackEffect(-1)]
    public void Replace(int index)
    {
        lua_copy(Pointer, -1, index);
        lua_settop(Pointer, -2);
    }

    /// <summary>
    ///     Copies the value at <paramref name="fromIndex" /> into the slot <paramref name="toIndex" /> without moving
    ///     anything (<c>lua_copy</c>).
    /// </summary>
    /// <param name="fromIndex">A valid index.</param>
    /// <param name="toIndex">A valid index.</param>
    [LuaStackEffect(0)]
    public void Copy(int fromIndex, int toIndex)
    {
        lua_copy(Pointer, fromIndex, toIndex);
    }

    /// <summary>
    ///     Rotates the elements between <paramref name="index" /> and the top by <paramref name="count" /> positions
    ///     towards the top, or towards the bottom when negative (<c>lua_rotate</c>).
    /// </summary>
    /// <param name="index">A valid index that is not a pseudo-index.</param>
    /// <param name="count">Positions; its magnitude must not exceed the size of the rotated slice.</param>
    [LuaStackEffect(0)]
    public void Rotate(int index, int count)
    {
        lua_rotate(Pointer, index, count);
    }
}
