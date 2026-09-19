using System;
using CESDK.Annotations.Lifetime;
using CESDK.Annotations.Threading;

namespace CESDK.Annotations.Lua;

/// <summary>
///     Binds a <see langword="partial" /> method or property to a global of Cheat Engine's Lua environment; the body is
///     generated.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by.</b> The <c>CESDK.SourceGenerators.LuaBindings</c> generator, which discovers the member with
///         <c>ForAttributeWithMetadataName("CESDK.Annotations.Lua.LuaGlobalAttribute")</c>. Its contract is the
///         implementing
///         half of the partial declaration. On a method the global is a function: the generated body records the stack
///         top,
///         pushes the global and the arguments, makes one protected call, reads the result and restores the stack on every
///         path, with the name emitted as a UTF-8 literal and never looked up through a managed string at run time.
///         A partial property that carries the attribute gets no generated accessors. Which signatures are supported is
///         documented with the generator. A declaration it cannot
///         implement (not partial, unsupported parameter or return types) is a generator-input error
///         (CESDK2004).
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour and nothing in the SDK reads it: the generated body carries the
///         name. It stays in metadata unconditionally so that tooling can read it from a compiled assembly. Whether the
///         bound member may be used before the plugin is enabled, or off the main thread, is declared separately with
///         <see cref="RequiresPluginEnabledAttribute" /> and <see cref="MainThreadOnlyAttribute" />. Instances are
///         immutable
///         and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="false" />: the attribute is an
///         instruction to generate the body of the one declaration that carries it; an override is another declaration
///         with
///         a body of its own. <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />: one body
///         calls
///         one global. <see cref="AttributeTargets.Method" /> also admits property accessors, local functions and lambdas,
///         which cannot be partial; rejecting them is the generator's job, the compiler accepts them.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class LuaGlobalAttribute : Attribute
{
    /// <summary>
    ///     Initializes the attribute with the name of the Lua global to bind.
    /// </summary>
    /// <param name="name">
    ///     The global name, spelled exactly as Cheat Engine registers it (Lua names are case-sensitive; the external
    ///     spelling is contract, for example <c>readInteger</c>). Must not be <see langword="null" /> or empty.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> is empty.</exception>
    /// <remarks>
    ///     The constructor only runs when something materialises the attribute through reflection. The compiler stores
    ///     the argument without executing this check, so <c>[LuaGlobal(null!)]</c> and <c>[LuaGlobal("")]</c> compile; a
    ///     generator reads a <see langword="null" /> constant or an empty string and has to validate the name itself.
    /// </remarks>
    public LuaGlobalAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>
    ///     Gets the name of the bound Lua global. Never <see langword="null" /> or empty.
    /// </summary>
    public string Name { get; }
}
