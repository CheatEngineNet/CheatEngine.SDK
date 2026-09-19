using System;

namespace CESDK.Annotations.Lua;

/// <summary>
///     Binds a <see langword="partial" /> property of a <see cref="LuaClassAttribute">Lua class wrapper</see> to a
///     property of the wrapped Cheat Engine object; the accessors are generated.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by.</b> Nothing in the SDK reads this attribute: it generates nothing, so a partial property that
///         relies on it has no implementation. The attribute only supplies the external name (<see cref="Name" />) of the
///         member of the wrapped object.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour and nothing in the SDK reads it: generated accessors carry the
///         name themselves. It stays in metadata unconditionally so that tooling can read it from a compiled assembly.
///         Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="false" />: the attribute asks
///         for
///         the accessors of the one declaration that carries it; an override is another declaration with accessors of its
///         own. <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />: one managed property
///         maps to
///         one Cheat Engine property.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LuaPropertyAttribute : Attribute
{
    /// <summary>
    ///     Initializes the attribute with the name of the Cheat Engine property to bind.
    /// </summary>
    /// <param name="name">
    ///     The property name as Cheat Engine's Lua object model spells it, for example <c>Count</c>; the external
    ///     spelling is contract. Must not be <see langword="null" /> or empty.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> is empty.</exception>
    /// <remarks>
    ///     The constructor only runs when something materialises the attribute through reflection. The compiler stores
    ///     the argument without executing this check, so <c>[LuaProperty(null!)]</c> and <c>[LuaProperty("")]</c> compile;
    ///     a generator reads a <see langword="null" /> constant or an empty string and has to validate the name itself.
    /// </remarks>
    public LuaPropertyAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>
    ///     Gets the name of the bound Cheat Engine property. Never <see langword="null" /> or empty.
    /// </summary>
    public string Name { get; }
}
