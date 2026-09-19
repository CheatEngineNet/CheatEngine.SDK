using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Lua.Tests.Generated;

/// <summary>
///     The worked example of the generated call shape: the declarations a plugin author (or the EngineApi spec) writes,
///     and below them the exact bodies the LuaBindings generator is expected to emit against this assembly's API.
///     Hand-written here so that the shapes compile and run independently of the generator; the generator's output for
///     these declarations must match these bodies (modulo <c>global::</c> qualification and the generated-code header).
/// </summary>
internal static partial class MemoryBindings
{
    /// <summary>Reads a 32-bit integer from the target process through Cheat Engine's <c>readInteger</c>.</summary>
    /// <returns>
    ///     <see langword="false" /> when the address is unreadable (<c>readInteger</c> returned <c>nil</c>), the global
    ///     is missing, or the call raised.
    /// </returns>
    [LuaGlobal("readInteger")]
    private static partial bool TryReadInt32Raw(nuint address, bool signed, out int value);

    public static bool TryReadInt32(nuint address, out int value) => TryReadInt32Raw(address, true, out value);

    /// <summary>The throwing form of the same binding: every failure is a <see cref="LuaException" />.</summary>
    [LuaGlobal("readInteger")]
    private static partial int ReadInt32Raw(nuint address, bool signed);

    public static int ReadInt32(nuint address) => ReadInt32Raw(address, true);
}

// ---- what the generator emits -------------------------------------------------------------------------------------
internal static partial class MemoryBindings
{
    private static readonly LuaRef s_readInteger = new();

    private static partial bool TryReadInt32Raw(nuint address, bool signed, out int value)
    {
        var L = LuaRuntime.AcquireState(); // one state acquisition per operation
        var top = L.Top; // explicit settop: no EH region on the success path
        if (!LuaGlobalFunctions.TryPush(L, s_readInteger,
                "readInteger"u8)) // rawgeti on the cached ref; resolve + type-check + luaL_ref on first use
            return LuaCallSupport.Fail(L, top, out value); // cold, NoInlining: restore top, default the result

        AddressMarshaller.Push(L, address); // pushinteger of the address bits
        BooleanMarshaller.Push(L, signed);
        if (!L.TryCall(2, 1).IsOk) // lua_pcallk(L, 2, 1, 0, 0, null)
            return LuaCallSupport.Fail(L, top, out value); // the error value is discarded with the frame

        var ok = Int32Marshaller.TryRead(L, -1, out value); // tointegerx + range check; nil => false, no exception
        L.SetTop(top);
        return ok;
    }

    // The throwing form has the same three exits, each a [DoesNotReturn] cold helper that restores the stack first.
    private static partial int ReadInt32Raw(nuint address, bool signed)
    {
        var L = LuaRuntime.AcquireState();
        var top = L.Top;
        if (!LuaGlobalFunctions.TryPush(L, s_readInteger, "readInteger"u8))
            LuaCallSupport.ThrowUnresolvedGlobal(L, top, "readInteger"); // exit 1: no such function

        AddressMarshaller.Push(L, address);
        BooleanMarshaller.Push(L, signed);
        var status = L.TryCall(2, 1);
        if (!status.IsOk) LuaCallSupport.Throw(L, top, status); // exit 2: the call raised

        if (!Int32Marshaller.TryRead(L, -1, out var value))
            LuaCallSupport.ThrowUnexpectedResult(L, top, -1, "readInteger", "an integer"); // exit 3: nil or wrong type

        L.SetTop(top);
        return value;
    }
}
