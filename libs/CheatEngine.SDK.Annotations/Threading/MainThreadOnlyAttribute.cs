using System;

namespace CheatEngine.SDK.Annotations.Threading;

/// <summary>
///     Declares that an API may only be used on Cheat Engine's main thread.
/// </summary>
/// <remarks>
///     <para>
///         <b>Meaning.</b> On a method, property or constructor: that member must be called on the main thread. On a
///         class,
///         struct or interface: every member the type declares, constructors and static members included, must be. Nested
///         types are not covered and are annotated on their own. The body of an annotated member can only be entered on
///         the
///         main thread, so it is itself a main-thread context, exactly as if it carried
///         <see cref="RunsOnMainThreadAttribute" />.
///     </para>
///     <para>
///         <b>Consumed by.</b> Nothing in the SDK reads this attribute. It documents that an API belongs to the main
///         thread and reports no diagnostic when it is used elsewhere.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour: it neither moves a call to the main thread nor checks the
///         calling thread. It stays in metadata unconditionally, because an analyzer has to read it from referenced,
///         already
///         compiled assemblies. Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="true" />: the affinity belongs
///         to
///         the native object behind the API, so an override or a derived wrapper type cannot shed it. Roslyn does not
///         apply
///         attribute inheritance on its own (<c>ISymbol.GetAttributes()</c> returns what is declared on that symbol only):
///         a
///         consumer walks overridden members and base types. Attribute inheritance never flows from an interface to its
///         implementations: the annotation on an interface governs calls made through the interface, and an implementing
///         type that is also used directly repeats it. <see cref="AttributeUsageAttribute.AllowMultiple" /> is
///         <see langword="false" />: the attribute is a marker without arguments, a second instance could add nothing.
///         <see cref="AttributeTargets.Method" /> also admits property accessors, for a property whose setter alone is
///         thread-affine.
///     </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method
    | AttributeTargets.Property
    | AttributeTargets.Constructor
    | AttributeTargets.Class
    | AttributeTargets.Struct
    | AttributeTargets.Interface)]
public sealed class MainThreadOnlyAttribute : Attribute;
