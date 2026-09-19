using System;

namespace CESDK.Annotations.Lua;

/// <summary>
///     Binds a <see langword="partial" /> method of a <see cref="LuaClassAttribute">Lua class wrapper</see> to a method
///     of the wrapped Cheat Engine object; the body is generated.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by.</b> Nothing in the SDK reads this attribute: it generates nothing, so a partial method that
///         relies on it has no implementation. The attribute only supplies the external name (<see cref="Name" />) of the
///         member of the wrapped object.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour and nothing in the SDK reads it: a generated body carries the
///         name itself. It stays in metadata unconditionally so that tooling can read it from a compiled assembly.
///         Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="false" />: the attribute asks
///         for
///         the body of the one declaration that carries it; an override is another declaration with a body of its own.
///         <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />: one body calls one member.
///         <see cref="AttributeTargets.Method" /> also admits property accessors, local functions and lambdas, which
///         cannot
///         be partial; rejecting them is the generator's job, the compiler accepts them.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class LuaMethodAttribute : Attribute
{
    /// <summary>
    ///     Initializes the attribute with the name of the Cheat Engine method to bind.
    /// </summary>
    /// <param name="name">
    ///     The method name as Cheat Engine's Lua object model spells it, for example <c>firstScan</c>; the external
    ///     spelling is contract. Must not be <see langword="null" /> or empty.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> is empty.</exception>
    /// <remarks>
    ///     The constructor only runs when something materialises the attribute through reflection. The compiler stores
    ///     the argument without executing this check, so <c>[LuaMethod(null!)]</c> and <c>[LuaMethod("")]</c> compile; a
    ///     generator reads a <see langword="null" /> constant or an empty string and has to validate the name itself.
    /// </remarks>
    public LuaMethodAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>
    ///     Gets the name of the bound Cheat Engine method. Never <see langword="null" /> or empty.
    /// </summary>
    public string Name { get; }
}
