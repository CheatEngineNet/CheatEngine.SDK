using System;

namespace CheatEngine.SDK.Annotations.Lua;

/// <summary>
///     Declares that a <see langword="readonly" /> <see langword="partial" /> struct is the borrowed managed handle for
///     a class of Cheat Engine's Lua object model.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by.</b> <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c> generates the
///         <c>CEObject</c> storage, <c>ICEObject&lt;T&gt;.FromHandle</c>, pointer-identity equality and Lua marshalling
///         members for the annotated handle. It names the Cheat Engine class the type wraps (<see cref="Name" />).
///         The handle is always borrowed; <c>Owned&lt;T&gt;</c>, not a class-versus-struct distinction, represents a
///         plugin-owned object that must be destroyed.
///     </para>
///     <para>
///         <b>Run time.</b> The attribute has no behaviour itself. Generated members use the protected
///         <c>CEObject</c> primitives and stay in metadata so tooling can map a compiled wrapper type back to its
///         Cheat Engine class. Instances are immutable and may be used from any thread.
///     </para>
///     <para>
///         <b>Usage.</b> <see cref="AttributeUsageAttribute.Inherited" /> is <see langword="false" />: a derived wrapper
///         stands for a different, more derived Cheat Engine class and has to name it; inheriting the attribute would make
///         it claim the name of its base. <see cref="AttributeUsageAttribute.AllowMultiple" /> is <see langword="false" />
///         :
///         a wrapper type stands for exactly one Cheat Engine class.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class LuaClassAttribute : Attribute
{
	/// <summary>
	///     Initializes the attribute with the name of the wrapped Cheat Engine class.
	/// </summary>
	/// <param name="name">
	///     The class name as Cheat Engine's Lua object model spells it, for example <c>MemScan</c>. Must not be
	///     <see langword="null" /> or empty.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="name" /> is empty.</exception>
	/// <remarks>
	///     The constructor only runs when something materialises the attribute through reflection. The compiler stores
	///     the argument without executing this check, so <c>[LuaClass(null!)]</c> and <c>[LuaClass("")]</c> compile; a
	///     generator reads a <see langword="null" /> constant or an empty string and has to validate the name itself.
	/// </remarks>
	public LuaClassAttribute(string name)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		Name = name;
	}

	/// <summary>
	///     Gets the name of the wrapped Cheat Engine class. Never <see langword="null" /> or empty.
	/// </summary>
	public string Name
	{
		get;
	}
}
