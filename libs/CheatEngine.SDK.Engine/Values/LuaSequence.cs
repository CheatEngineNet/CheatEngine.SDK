using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Values;

/// <summary>
///     Zero-based access to a Lua sequence (a table returned by a Cheat Engine function, whose elements are keyed
///     <c>1 .. n</c>): the only place in this assembly where a Lua key is derived from an index. Callers pass the index
///     they would use on a C# array; the conversion is <see cref="IndexBase.ToLuaKey" />.
/// </summary>
/// <remarks>
///     The protected members honour metamethods and report failure as a <see cref="LuaStatus" /> with the error value on
///     top of the stack, like the <see cref="LuaState" /> members they call; the raw members never run Lua code and are
///     for tables that are known to be plain. A negative index is a programmer error and throws
///     <see cref="System.ArgumentOutOfRangeException" /> before anything is pushed.
/// </remarks>
public static class LuaSequence
{
    /// <param name="state">The state.</param>
    extension(LuaState state)
    {
        /// <summary>
        ///     Pushes element <paramref name="zeroBasedIndex" /> of the sequence at <paramref name="tableIndex" /> under
        ///     protection (<c>t[i + 1]</c>). Stack after success: the value (<c>nil</c> past the end).
        /// </summary>
        /// <param name="tableIndex">A valid index of the sequence; a relative index is taken before anything is pushed.</param>
        /// <param name="zeroBasedIndex">The element's zero-based position.</param>
        /// <returns>The status; on failure one error value is on top.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
        public LuaStatus TryGetSequenceItem(int tableIndex, int zeroBasedIndex)
        {
            return state.TryGetIndex(tableIndex, IndexBase.ToLuaKey(zeroBasedIndex));
        }

        /// <summary>
        ///     Pops the value on top and stores it as element <paramref name="zeroBasedIndex" /> of the sequence at
        ///     <paramref name="tableIndex" /> under protection (<c>t[i + 1] = v</c>).
        /// </summary>
        /// <param name="tableIndex">
        ///     A valid index of the sequence, which must not be the top; a relative index is taken before
        ///     anything is pushed.
        /// </param>
        /// <param name="zeroBasedIndex">The element's zero-based position.</param>
        /// <returns>The status; on failure one error value is on top in place of the value.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
        public LuaStatus TrySetSequenceItem(int tableIndex, int zeroBasedIndex)
        {
            return state.TrySetIndex(tableIndex, IndexBase.ToLuaKey(zeroBasedIndex));
        }

        /// <summary>
        ///     Pushes element <paramref name="zeroBasedIndex" /> of the plain table at <paramref name="tableIndex" /> without
        ///     metamethods (<c>lua_rawgeti</c> with key <c>i + 1</c>).
        /// </summary>
        /// <param name="tableIndex">A valid index of a table.</param>
        /// <param name="zeroBasedIndex">The element's zero-based position.</param>
        /// <returns>The type of the pushed value; <see cref="LuaType.Nil" /> past the end.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
        [LuaStackEffect(1)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LuaType RawGetSequenceItem(int tableIndex, int zeroBasedIndex)
        {
            return state.RawGetIndex(tableIndex, IndexBase.ToLuaKey(zeroBasedIndex));
        }

        /// <summary>
        ///     Pops the value on top and stores it as element <paramref name="zeroBasedIndex" /> of the plain table at
        ///     <paramref name="tableIndex" /> without metamethods (<c>lua_rawseti</c> with key <c>i + 1</c>).
        /// </summary>
        /// <param name="tableIndex">A valid index of a table.</param>
        /// <param name="zeroBasedIndex">The element's zero-based position.</param>
        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
        /// <remarks>Allocates inside Lua when the table grows.</remarks>
        [LuaStackEffect(-1)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RawSetSequenceItem(int tableIndex, int zeroBasedIndex)
        {
            state.RawSetIndex(tableIndex, IndexBase.ToLuaKey(zeroBasedIndex));
        }

        /// <summary>
        ///     The number of elements of the sequence at <paramref name="tableIndex" /> without metamethods
        ///     (<c>lua_rawlen</c>: the border of the table, which is its element count for a proper sequence).
        /// </summary>
        /// <param name="tableIndex">A valid index of a table.</param>
        /// <returns>The count, which is also one past the last valid zero-based index.</returns>
        /// <exception cref="System.OverflowException">The table has more than <see cref="int.MaxValue" /> elements.</exception>
        [LuaStackEffect(0)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int RawSequenceCount(int tableIndex)
        {
            return checked((int)state.RawLength(tableIndex));
        }
    }
}
