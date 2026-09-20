using System;

namespace CheatEngine.SDK.Annotations.Lifetime;

/// <summary>
///     Declares that a value refers to an object that Cheat Engine owns: the receiver may use it, but must not dispose
///     or destroy it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Meaning.</b> On a return value (<c>[return: CEOwned]</c>) or a property: the object handed out belongs to
///         Cheat Engine, for example the address list of the main window or the scanner behind the GUI. On a parameter:
///         the
///         object passed in belongs to Cheat Engine, which covers <see langword="out" /> parameters of <c>Try</c> methods
///         and
///         the arguments of callbacks and event handlers. Most borrowed objects need no annotation because their wrapper
///         type is a handle that cannot be disposed at all; the attribute exists for the remaining case, an API that hands
///         out a borrowed instance of a type whose other instances are owned and disposable. Destroying such an object
///         leaves Cheat Engine with a dangling pointer.
///     </para>
///     <para>
///         <b>Consumed by.</b> The SDK analyzer follows values explicitly marked as borrowed and reports direct
///         <c>Dispose</c> or <c>DisposeAsync</c> calls on them (CESDK1003). The attribute also documents ownership for
///         callers and tooling.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour: it does not make the wrapper refuse disposal. It stays in
///         metadata unconditionally, because an analyzer has to read it from referenced, already compiled assemblies.
///         Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="true" />: who owns the object
///         an
///         API hands out is part of that API's contract, so an override hands out objects under the same terms. Roslyn
///         does
///         not apply attribute inheritance on its own: a consumer walks the chain of overridden members.
///         <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />: the attribute is a marker
///         without
///         arguments. Fields are not a target on purpose: storing a borrowed object does not change who owns it, so the
///         statement belongs on the API that hands the object out.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = true,
    AllowMultiple = false)]
public sealed class CEOwnedAttribute : Attribute;
