using System;

namespace CheatEngine.SDK.Annotations.Lua;

/// <summary>
///     Exports a managed static method to Cheat Engine's Lua environment as a global C function.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by.</b> The <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c> generator, which discovers the
///         method with
///         <c>ForAttributeWithMetadataName("CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute")</c>. Its contract
///         for an annotated method is an <c>[UnmanagedCallersOnly]</c> <c>cdecl</c> thunk of the <c>lua_CFunction</c>
///         shape that reads the arguments from the Lua stack, calls the method inside a catch-all, pushes the result
///         and reports failures through the SDK's managed-to-Lua error channel, plus an entry in a generated
///         registration table that binds the thunk to <see cref="Name" />. Which method shapes and parameter types it
///         accepts is documented with the generator. A method it cannot export (not static, generic, unsupported
///         parameter types) is a generator-input error (CESDK2003). Two otherwise valid exports with the same name in
///         one binding type are CESDK2005. CESDK2001 is the error for a compilation that does not allow the unsafe code
///         the thunks need.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour and nothing in the SDK reads it: registration is done by the
///         generated table. It stays in metadata unconditionally so that tooling can read it from a compiled assembly. An
///         exported function runs on whichever thread executes the Lua code that calls it, which is not necessarily Cheat
///         Engine's main thread. Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="false" />: the export belongs
///         to
///         the one declaration that carries the attribute in source, which is all a generator sees, and a static method
///         has
///         no overrides to pass it to. <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />:
///         one
///         method yields one thunk under one name; an alias is a second method that forwards to the first.
///         <see cref="AttributeTargets.Method" /> also admits property accessors, local functions and lambdas, none of
///         which
///         can be exported; rejecting them is the generator's job, the compiler accepts them.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class LuaFunctionAttribute : Attribute
{
    /// <summary>
    ///     Initializes the attribute with the name of the Lua global that receives the function.
    /// </summary>
    /// <param name="name">
    ///     The global name, exactly as Lua scripts will spell it (Lua names are case-sensitive). Must not be
    ///     <see langword="null" /> or empty.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> is empty.</exception>
    /// <remarks>
    ///     The constructor only runs when something materialises the attribute through reflection. The compiler stores
    ///     the argument without executing this check, so <c>[LuaFunction(null!)]</c> and <c>[LuaFunction("")]</c> compile;
    ///     a generator reads a <see langword="null" /> constant or an empty string and has to validate the name itself.
    /// </remarks>
    public LuaFunctionAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>
    ///     Gets the name of the Lua global under which the function is registered. Never <see langword="null" /> or
    ///     empty.
    /// </summary>
    public string Name { get; }
}
