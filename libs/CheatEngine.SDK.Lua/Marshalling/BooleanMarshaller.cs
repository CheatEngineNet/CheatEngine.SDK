using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="bool" /> as a Lua boolean. Reading is strict: only a Lua <c>true</c> or <c>false</c> succeeds, so that a
///     <c>nil</c> result (Cheat Engine's "failed") is not mistaken for <see langword="false" />. For Lua truthiness of an
///     arbitrary value use <see cref="LuaState.ToBoolean" />.
/// </summary>
/// <remarks>One C API call to push, two to read (type, then value); allocates nothing.</remarks>
public readonly struct BooleanMarshaller : ILuaMarshaller<bool>
{
    /// <inheritdoc />
    [LuaStackEffect(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Push(LuaState state, bool value)
    {
        state.PushBoolean(value);
    }

    /// <inheritdoc />
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryRead(LuaState state, int index, out bool value)
    {
        if (state.TypeOf(index) != LuaType.Boolean)
        {
            value = false;
            return false;
        }

        value = state.ToBoolean(index);
        return true;
    }
}
