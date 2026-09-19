using System.Runtime.CompilerServices;
using CESDK.Annotations.Lua;
using CESDK.Lua.State;

namespace CESDK.Lua.Marshalling;

/// <summary>
///     <see cref="long" /> as a Lua integer (<c>lua_Integer</c> is 64-bit in Lua 5.3): the lossless scalar marshaller.
/// </summary>
/// <remarks>
///     Reading follows Lua's own conversion (<c>lua_tointegerx</c>): an integer, a float with an exact integral value
///     (<c>3.0</c>), or a string Lua can convert (<c>"42"</c>, <c>"0x10"</c>) all succeed; <c>2.5</c>, <c>nil</c>, a
///     boolean or a table fail. Use <see cref="LuaState.IsInteger" /> when the representation itself matters.
///     One C API call each way; allocates nothing.
/// </remarks>
public readonly struct Int64Marshaller : ILuaMarshaller<long>
{
    /// <inheritdoc />
    [LuaStackEffect(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Push(LuaState state, long value)
    {
        state.PushInteger(value);
    }

    /// <inheritdoc />
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryRead(LuaState state, int index, out long value)
    {
        return state.TryReadInteger(index, out value);
    }
}
