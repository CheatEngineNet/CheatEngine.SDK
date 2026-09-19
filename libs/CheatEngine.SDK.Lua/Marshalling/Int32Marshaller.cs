using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="int" /> as a Lua integer. Reading succeeds only when the 64-bit Lua integer fits a 32-bit signed value;
///     out-of-range values are reported as <see langword="false" />, never truncated.
/// </summary>
/// <remarks>
///     Conversion rules for the Lua side are those of <see cref="Int64Marshaller" />. One C API call each way plus a
///     range check; allocates nothing.
/// </remarks>
public readonly struct Int32Marshaller : ILuaMarshaller<int>
{
    /// <inheritdoc />
    [LuaStackEffect(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Push(LuaState state, int value)
    {
        state.PushInteger(value);
    }

    /// <inheritdoc />
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryRead(LuaState state, int index, out int value)
    {
        if (state.TryReadInteger(index, out var wide) && wide >= int.MinValue && wide <= int.MaxValue)
        {
            value = (int)wide;
            return true;
        }

        value = 0;
        return false;
    }
}
