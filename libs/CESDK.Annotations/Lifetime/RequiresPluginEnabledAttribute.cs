using System;

namespace CESDK.Annotations.Lifetime;

/// <summary>
///     Declares that an API is unusable until Cheat Engine has enabled the plugin.
/// </summary>
/// <remarks>
///     <para>
///         <b>Meaning.</b> The SDK receives its connection to the host (the exported function table and, through it, the
///         Lua
///         state) when Cheat Engine calls the plugin's <c>EnablePlugin</c> callback. Before that moment an annotated API
///         has
///         nothing to talk to. On a method, property or constructor the statement covers that member; on a class or struct
///         it covers every member the type declares, constructors and static members included. Nested types are not
///         covered
///         and are annotated on their own.
///     </para>
///     <para>
///         <b>Consumed by.</b> Nothing in the SDK reads this attribute. It documents that an API needs an enabled plugin
///         and reports no diagnostic when the API is used too early.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour and does not guard the call; how an annotated API fails when it
///         is used too early is documented on that API. It stays in metadata unconditionally, because an analyzer has to
///         read it from referenced, already compiled assemblies. Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="true" />: the requirement
///         comes
///         from what the API needs underneath, so an override or a derived type needs it too. Roslyn does not apply
///         attribute inheritance on its own (<c>ISymbol.GetAttributes()</c> returns what is declared on that symbol only):
///         a
///         consumer walks overridden members and base types. <see cref="AttributeUsageAttribute.AllowMultiple" /> is
///         <see langword="false" />: the attribute is a marker without arguments, a second instance could add nothing.
///         <see cref="AttributeTargets.Method" /> also admits property accessors, for a property of which only one
///         accessor
///         reaches the host.
///     </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method
    | AttributeTargets.Property
    | AttributeTargets.Constructor
    | AttributeTargets.Class
    | AttributeTargets.Struct)]
public sealed class RequiresPluginEnabledAttribute : Attribute;
