using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Protected bindings for CE's user-defined symbol registry and address-name formatter.</summary>
/// <remarks>
///     <para>
///         <b>Provenance and compatibility.</b> This surface binds exactly the Client source-record call shapes for
///         <c>registerSymbol</c>, <c>unregisterSymbol</c>, and <c>getNameFromAddress</c>. It deliberately does not add
///         unqualified optional arguments or a managed name-selection policy.
///     </para>
///     <para>
///         <b>Lifetime and ownership.</b> Lookup returns a newly allocated managed string and owns no Lua reference or
///         CE object. Registering a symbol mutates CE's host-wide symbol table; it does not yield an independently owned
///         CE resource, and this class intentionally does not claim exclusive ownership of a name. A caller that creates
///         a registration must retain its <see cref="SymbolName" /> and issue its own qualified cleanup call.
///     </para>
///     <para>
///         Calls are generated through the SDK's protected, attach-epoch-aware Lua binding path. They restore the Lua
///         stack and return <see cref="LuaOperationStatus" /> without parsing error text.
///     </para>
/// </remarks>
public static partial class SymbolRegistry
{
    /// <summary>Gets CE's formatted name for a target-process address with CE's default name sources.</summary>
    /// <param name="address">The target-process address passed to CE as the sole argument.</param>
    /// <param name="name">A copied managed string only when the returned status is successful.</param>
    /// <returns>The protected binding outcome.</returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [LuaGlobal("getNameFromAddress")]
    [RequiresPluginEnabled]
    public static partial LuaOperationStatus TryGetName([LuaMarshaller(typeof(Address))] Address address,
        out string? name);

    /// <summary>Registers a user-defined symbol at a target-process address.</summary>
    /// <param name="name">The registration name CE will add to its symbol handler.</param>
    /// <param name="address">The target-process address associated with <paramref name="name" />.</param>
    /// <param name="options">The persistence option for the registration.</param>
    /// <returns>The protected binding outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="name" /> is default or otherwise invalid.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static LuaOperationStatus Register(SymbolName name, Address address,
        SymbolRegistrationOptions options = default)
    {
        ValidateName(name);
        return RegisterCore(name, address, options.DoNotSave);
    }

    /// <summary>Removes a user-defined symbol name from CE's symbol handler.</summary>
    /// <param name="name">The registration name to remove.</param>
    /// <returns>The protected binding outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="name" /> is default or otherwise invalid.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static LuaOperationStatus Unregister(SymbolName name)
    {
        ValidateName(name);
        return UnregisterCore(name);
    }

    [LuaGlobal("registerSymbol")]
    private static partial LuaOperationStatus RegisterCore([LuaMarshaller(typeof(SymbolName))] SymbolName name,
        [LuaMarshaller(typeof(Address))] Address address, bool doNotSave);

    [LuaGlobal("unregisterSymbol")]
    private static partial LuaOperationStatus UnregisterCore([LuaMarshaller(typeof(SymbolName))] SymbolName name);

    private static void ValidateName(SymbolName name)
    {
        if (string.IsNullOrWhiteSpace(name.Value))
            throw new ArgumentException("A symbol name must not be default, empty or white space.", nameof(name));
    }
}
