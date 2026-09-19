using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Lua.Tests.Generated;

/// <summary>
///     The worked example for a <b>string result</b>. A generated body restores the stack before it returns, so a
///     <c>ReadOnlySpan&lt;byte&gt;</c> read from the result would point into a Lua string that may already be collected:
///     spans are argument-only in generated bodies. The two result shapes the generator may emit are shown here, in the
///     order of preference: copy-out into a caller buffer (allocation-free), and a managed <see cref="string" />.
/// </summary>
internal static partial class StringBindings
{
    /// <summary>Reads a string from the target process through Cheat Engine's <c>readString</c>, into the caller's buffer.</summary>
    /// <returns>
    ///     <see langword="false" /> when the read failed (<c>nil</c>), the text does not fit
    ///     <paramref name="destination" />, the global is missing, or the call raised.
    /// </returns>
    [LuaGlobal("readString")]
    public static partial bool TryReadString(nuint address, int maxLength, Span<byte> destination, out int written);

    /// <summary>The allocating convenience: the same call, decoded into a <see cref="string" />.</summary>
    [LuaGlobal("readString")]
    public static partial bool TryReadString(nuint address, int maxLength, [MaybeNullWhen(false)] out string value);
}

// ---- what the generator emits -------------------------------------------------------------------------------------
internal static partial class StringBindings
{
    private static readonly LuaRef s_readString = new();

    public static partial bool TryReadString(nuint address, int maxLength, Span<byte> destination, out int written)
    {
        var L = LuaRuntime.AcquireState();
        var top = L.Top;
        if (!LuaGlobalFunctions.TryPush(L, s_readString, "readString"u8))
            return LuaCallSupport.Fail(L, top, out written);

        AddressMarshaller.Push(L, address);
        Int32Marshaller.Push(L, maxLength);
        if (!L.TryCall(2, 1).IsOk) return LuaCallSupport.Fail(L, top, out written);

        var ok = L.TryCopyUtf8(-1, destination, out written); // the copy happens while the string is still on the stack
        L.SetTop(top);
        return ok;
    }

    // The [MaybeNullWhen(false)] of the declaring part applies here too (attributes of partial parts are merged, so
    // the implementing part must not repeat it: CS0579).
    public static partial bool TryReadString(nuint address, int maxLength, out string value)
    {
        var L = LuaRuntime.AcquireState();
        var top = L.Top;
        if (!LuaGlobalFunctions.TryPush(L, s_readString, "readString"u8)) return LuaCallSupport.Fail(L, top, out value);

        AddressMarshaller.Push(L, address);
        Int32Marshaller.Push(L, maxLength);
        if (!L.TryCall(2, 1).IsOk) return LuaCallSupport.Fail(L, top, out value);

        var ok = StringMarshaller.TryRead(L, -1,
            out value); // decodes into a new string: the one allocation of this shape
        L.SetTop(top);
        return ok;
    }
}
