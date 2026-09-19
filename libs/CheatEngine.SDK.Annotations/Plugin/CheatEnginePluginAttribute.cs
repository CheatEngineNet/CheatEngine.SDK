using System;

namespace CheatEngine.SDK.Annotations.Plugin;

/// <summary>
///     Designates the class that Cheat Engine runs as the plugin of the assembly, and states the name under which the
///     plugin reports itself to Cheat Engine.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by.</b> The <c>CheatEngine.SDK.SourceGenerators.EntryPoint</c> generator finds the class with
///         <c>ForAttributeWithMetadataName("CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute")</c> and
///         emits, into the plugin assembly, the <c>CESDK.CESDK.CEPluginInitialize</c> bootstrap that Cheat Engine
///         requires and a factory that constructs the class with <see langword="new" /> through its parameterless
///         constructor (no reflection) and exposes <see cref="Name" /> as a UTF-8 literal.
///         <c>CheatEngine.SDK.Analyzers</c> uses the same attribute to check that the class can be constructed that way
///         and that the name is usable (CESDK0001), that a compilation holds at most one plugin class (CESDK0002), and to
///         recognise a plugin assembly, in which the namespace <c>CESDK</c> and everything under it are reserved
///         (CESDK0004): inside them the simple name <c>CESDK</c> binds to the generated bootstrap class, so a qualified
///         name that starts with <c>CESDK.</c> no longer resolves to a namespace declared under <c>CESDK</c> (CS0426).
///         The SDK's own namespaces start with <c>CheatEngine.SDK</c> and are not affected.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour and nothing in the SDK reads it: the name reaches Cheat Engine
///         through generated code, never through reflection. It is still written to metadata unconditionally (it is not
///         <see cref="System.Diagnostics.ConditionalAttribute">conditional</see>), so that tooling can read it back from a
///         compiled assembly. Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="false" />: discovery is driven
///         by
///         what is written on a class declaration, so only that class is the plugin. With inheritance, a class deriving
///         from
///         a plugin class would count as a second plugin of the same assembly.
///         <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />: the record a plugin hands to
///         Cheat Engine has room for exactly one name.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CheatEnginePluginAttribute : Attribute
{
    /// <summary>
    ///     Initializes the attribute with the name the plugin reports to Cheat Engine.
    /// </summary>
    /// <param name="name">
    ///     The display name of the plugin. Must not be <see langword="null" /> or empty. Prefer ASCII: how Cheat Engine
    ///     decodes other characters in a plugin name has not been verified.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> is empty.</exception>
    /// <remarks>
    ///     The constructor only runs when something materialises the attribute through reflection. The compiler stores
    ///     the argument without executing this check, and generators and analyzers read it from there, so
    ///     <c>[CheatEnginePlugin(null!)]</c> and <c>[CheatEnginePlugin("")]</c> compile. The consumers validate the
    ///     argument themselves: a <see langword="null" />, empty or blank name is reported as CESDK0001 and no entry point
    ///     is generated for it.
    /// </remarks>
    public CheatEnginePluginAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>
    ///     Gets the name the plugin reports to Cheat Engine. Never <see langword="null" /> or empty.
    /// </summary>
    public string Name { get; }
}
