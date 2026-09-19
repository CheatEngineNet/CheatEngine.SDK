using System;
using System.Globalization;
using CESDK.Lua.Interop.Types;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace CESDK.Lua.Callbacks;

/// <summary>
///     The address of a managed <c>lua_CFunction</c>: a <see langword="static" /> method marked
///     <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c> with the signature <c>int (nint L)</c>. The
///     value that <see cref="LuaState.TryPushFunction" />, <see cref="LuaState.PushUncheckedFunction" /> and
///     <see cref="LuaCallback.TryCreate{TState}" /> turn into a Lua-callable closure.
/// </summary>
/// <remarks>
///     <para>
///         <b>Rules for the method behind it.</b> It runs on whichever thread executes the Lua code that calls it, with
///         the
///         state Lua passes (use that state, never <see cref="LuaRuntime.AcquireState" /> inside). It catches every
///         exception
///         and reports failures through <see cref="LuaThunk.Fail(LuaState,System.ReadOnlySpan{byte})" />:
///         an exception escaping
///         it
///         is undefined behaviour at the native boundary. It never calls a raising Lua API (<c>lua_error</c>, anything
///         that
///         can run a metamethod outside <c>lua_pcallk</c>). The constructor from a function pointer exists so that a
///         <c>&amp;Thunk</c> expression is checked by the compiler for signature and convention; it takes the state as
///         <see cref="nint" />, the shape generated thunks use, so that no raw <c>lua_State*</c> appears in this
///         assembly's
///         public surface. A thunk written against <c>lua_State*</c> is registered through its address, cast to
///         <see cref="nint" />.
///     </para>
///     <para>
///         Cheat Engine's <c>LuaRegister</c> export is never used for this: a C closure pushed with
///         <c>lua_pushcclosure</c>
///         and assigned with a protected set-global is the same registration with a convention that is portable to x86.
///     </para>
/// </remarks>
public readonly unsafe struct LuaNativeFunction : IEquatable<LuaNativeFunction>
{
    /// <summary>Wraps the address of a <c>cdecl</c> function <c>int (lua_State*)</c>; zero is the null function.</summary>
    /// <param name="address">The function address.</param>
    public LuaNativeFunction(nint address)
    {
        Address = address;
    }

    /// <summary>Wraps a typed function pointer whose parameter is the state as an integer (the shape generated thunks use).</summary>
    /// <param name="function">The function, usually <c>&amp;Thunk</c>.</param>
    public LuaNativeFunction(delegate* unmanaged[Cdecl]<nint, int> function)
    {
        Address = (nint)function;
    }

    /// <summary>Gets the function address; zero for the null function.</summary>
    public nint Address { get; }

    /// <summary>Gets a value indicating whether this is the null function, which must not be pushed.</summary>
    public bool IsNull => Address == 0;

    internal delegate* unmanaged[Cdecl]<lua_State*, int> Pointer =>
        (delegate* unmanaged[Cdecl]<lua_State*, int>)Address;

    /// <summary>Compares addresses.</summary>
    /// <param name="left">First function.</param>
    /// <param name="right">Second function.</param>
    public static bool operator ==(LuaNativeFunction left, LuaNativeFunction right)
    {
        return left.Address == right.Address;
    }

    /// <summary>Compares addresses.</summary>
    /// <param name="left">First function.</param>
    /// <param name="right">Second function.</param>
    public static bool operator !=(LuaNativeFunction left, LuaNativeFunction right)
    {
        return left.Address != right.Address;
    }

    /// <inheritdoc />
    public bool Equals(LuaNativeFunction other)
    {
        return Address == other.Address;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LuaNativeFunction other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Address.GetHashCode();
    }

    /// <summary><c>lua_CFunction@0x...</c>.</summary>
    public override string ToString()
    {
        return "lua_CFunction@0x" + Address.ToString("X", CultureInfo.InvariantCulture);
    }
}
