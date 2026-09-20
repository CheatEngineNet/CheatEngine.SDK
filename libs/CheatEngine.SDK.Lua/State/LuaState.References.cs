using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Lua.State;

// References use a private registry table. The host and other plugins own separate free lists.
public readonly unsafe partial struct LuaState
{
    /// <summary>
    ///     Pops the value on top and stores it in this SDK's private registry table, returning a reference stamped with the current
    ///     <see cref="LuaRuntime.CurrentStateIdentity" /> (<c>luaL_ref</c>).
    /// </summary>
    /// <returns>A new, resolved reference; the caller owns it and releases it with <see cref="LuaRef.Release" />.</returns>
    /// <remarks>
    ///     Allocates the <see cref="LuaRef" /> object and, inside Lua, possibly a registry slot. Meant for values that
    ///     are pushed many times: never call it per operation.
    /// </remarks>
    [LuaStackEffect(-1)]
    public LuaRef CreateRef()
    {
        using var operation = LuaRuntime.EnterStateOperation(this);
        var identity = LuaRuntime.CurrentStateIdentity;
        CheckProtectedResult(LuaReferences.Create(this, out var reference));
        return new LuaRef(reference, identity);
    }

    internal LuaStatus TryCreateRef(out LuaRef? reference)
    {
        using var operation = LuaRuntime.EnterStateOperation(this);
        var identity = LuaRuntime.CurrentStateIdentity;
        var status = LuaReferences.Create(this, out var slot);
        reference = status.IsOk ? new LuaRef(slot, identity) : null;
        return status;
    }

    /// <summary>
    ///     Pushes the value a reference designates (<c>lua_rawgeti</c> on the registry) when the reference is current.
    ///     Stack: +1 on <see langword="true" />, +0 on <see langword="false" />.
    /// </summary>
    /// <param name="reference">The reference; may be unresolved or stale, in which case nothing is pushed.</param>
    /// <returns><see langword="true" /> when the value was pushed.</returns>
    /// <remarks>Lookup and release share a gate, so a slot cannot be reused between validation and pushing its value.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPushRef(LuaRef reference)
    {
        using var operation = LuaRuntime.EnterStateOperation(this);
        lock (LuaReferences.Gate)
        {
            if (reference is null || !reference.TryGetCurrent(out var slot)) return false;
            return LuaReferences.Push(this, slot);
        }
    }
}
