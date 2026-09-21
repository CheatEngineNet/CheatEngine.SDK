using System;

namespace CheatEngine.SDK.Annotations.Lua;

/// <summary>
///     Selects the static Lua marshaller that a generated <see cref="LuaFunctionAttribute" /> thunk or
///     <see cref="LuaGlobalAttribute" /> wrapper uses for one parameter or return value.
/// </summary>
/// <remarks>
///     <para>
///         The LuaBindings generator validates that <see cref="MarshallerType" /> implements
///         <c>CheatEngine.SDK.Lua.Marshalling.ILuaMarshaller&lt;T&gt;</c> for the annotated declaration type and emits
///         direct calls to its static <c>Push</c> and <c>TryRead</c> members. It does not use reflection, a registry,
///         delegates or runtime type lookup.
///     </para>
///     <para>
///         Apply it to a by-value or <see langword="out" /> parameter, or use the return-target form
///         <c>[return: LuaMarshaller(typeof(MyMarshaller))]</c>. It is deliberately unavailable on a method as a
///         whole: each Lua value has one explicit conversion contract. A ref-like result remains invalid because a
///         generated wrapper restores the Lua stack before returning.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
public sealed class LuaMarshallerAttribute : Attribute
{
    /// <summary>Initializes the attribute with the concrete static marshaller type.</summary>
    /// <param name="marshallerType">A type implementing <c>ILuaMarshaller&lt;T&gt;</c> for the annotated value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="marshallerType" /> is <see langword="null" />.</exception>
    public LuaMarshallerAttribute(Type marshallerType)
    {
        ArgumentNullException.ThrowIfNull(marshallerType);
        MarshallerType = marshallerType;
    }

    /// <summary>Gets the concrete marshaller type named by this declaration.</summary>
    public Type MarshallerType { get; }
}
