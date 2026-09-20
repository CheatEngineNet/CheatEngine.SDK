using System;

namespace CheatEngine.SDK.Annotations.Lua;

/// <summary>
///     States by how many slots a method changes the height of the Lua stack it operates on.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by.</b> Nothing in the SDK reads this attribute. It documents the stack effect of a method, and
///         nothing compares a body with the effect it declares.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour: nothing checks the real stack against <see cref="Delta" />. It
///         stays in metadata unconditionally, because an analyzer has to read it from referenced, already compiled
///         assemblies. Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Contract.</b> <see cref="Delta" /> must hold on every path on which the method returns normally, including a
///         <see langword="false" /> result of a <c>Try</c> method. A method whose effect depends on its arguments or on
///         what
///         it finds on the stack must not carry the attribute.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="true" />: the effect is part
///         of
///         the behavioural contract of a virtual method and callers reason from the declaration they bind to, so every
///         override is held to the same value. Roslyn does not apply attribute inheritance on its own
///         (<c>ISymbol.GetAttributes()</c> returns what is declared on that symbol only): a consumer walks the chain of
///         overridden methods. Interface implementations are outside attribute inheritance altogether; a consumer maps an
///         implementation back to the annotated interface member, or the implementation repeats the attribute.
///         <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />: a method has one net effect.
///         <see cref="AttributeTargets.Method" /> includes property accessors, which is how a getter that pushes a value
///         declares it.
///     </para>
/// </remarks>
/// <remarks>
///     Initializes the attribute with the net change in stack height.
/// </remarks>
/// <param name="delta">
///     Slots on the stack after the method returns minus slots before it was called: positive when it pushes more
///     than it pops, negative when it pops more than it pushes, zero when it leaves the height unchanged. Every value
///     is accepted: the number is a claim for static analysis, there is nothing to validate it against here.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LuaStackEffectAttribute(int delta) : Attribute
{
    /// <summary>
    ///     Gets the net change in stack height: slots after the call minus slots before it.
    /// </summary>
    public int Delta { get; } = delta;
}
