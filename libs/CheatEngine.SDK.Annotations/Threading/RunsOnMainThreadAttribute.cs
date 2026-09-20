using System;

namespace CheatEngine.SDK.Annotations.Threading;

/// <summary>
///     Asserts that a body of code is known to execute on Cheat Engine's main thread.
/// </summary>
/// <remarks>
///     <para>
///         <b>Meaning.</b> On a method, property or event accessor, local function or lambda
///         (<c>[RunsOnMainThread] () => ...</c>): the body runs on the main thread, so it may use
///         <see cref="MainThreadOnlyAttribute">main-thread-only</see> APIs. The attribute places no requirement on
///         callers.
///         It fits code that the host or a dispatcher invokes: lifecycle methods, event handlers, callbacks. A method that
///         arbitrary code may call uses <see cref="MainThreadOnlyAttribute" /> instead, which restricts its callers and
///         makes
///         its body a main-thread context in one step.
///     </para>
///     <para>
///         <b>Consumed by.</b> Nothing in the SDK reads this attribute. It asserts that a body runs on the main thread,
///         and no diagnostic depends on it.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour and is not verified: a wrong assertion leaves the threading bug
///         in place. It stays in metadata unconditionally,
///         because an analyzer has to read it from referenced, already compiled assemblies, for example on a virtual SDK
///         method that plugin code overrides. Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="true" />: on a virtual method
///         the
///         statement is about who invokes that slot, so it holds for every override, and plugin authors do not repeat it
///         on
///         their overrides of annotated SDK methods. Roslyn does not apply attribute inheritance on its own: a consumer
///         walks the chain of overridden methods. For lambdas and local functions inheritance has no meaning.
///         <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />: the attribute is a marker
///         without
///         arguments. <see cref="AttributeTargets.Method" /> is the only target: it is the one under which C# accepts
///         attributes on lambdas, local functions and accessors. Parameters are not a target: a form on delegate-typed
///         parameters, by which a dispatcher API would declare that it invokes its argument on the main thread, is not
///         part of
///         the attribute, and adding a target later is a compatible change while removing one is not.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RunsOnMainThreadAttribute : Attribute;
