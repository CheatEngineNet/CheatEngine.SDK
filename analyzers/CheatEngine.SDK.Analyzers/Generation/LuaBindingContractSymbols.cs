using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.Analyzers.Generation;

/// <summary>
///     The well-known attribute types behind the CESDK2xxx rules, resolved once per compilation in the
///     compilation-start action of <see cref="LuaBindingAnalyzer" />. Immutable, safe for concurrent use. Either symbol
///     may be missing on its own (a project can reference <c>CheatEngine.SDK.Annotations</c> without ever declaring the
///     other
///     kind of binding); the analyzer registers as long as at least one resolves.
/// </summary>
/// <param name="luaFunctionAttribute">
///     The resolved <c>CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute</c>, or
///     <see langword="null" />.
/// </param>
/// <param name="luaGlobalAttribute">
///     The resolved <c>CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute</c>, or
///     <see langword="null" />.
/// </param>
/// <param name="luaMarshallerAttribute">The resolved custom-marshaller annotation, or <see langword="null" />.</param>
/// <param name="luaMarshallerContract">The resolved <c>ILuaMarshaller&lt;T&gt;</c> contract, or <see langword="null" />.</param>
/// <param name="luaClassAttribute">The resolved LuaClass marker, or <see langword="null" />.</param>
/// <param name="luaMethodAttribute">The resolved LuaMethod marker, or <see langword="null" />.</param>
/// <param name="luaPropertyAttribute">The resolved LuaProperty marker, or <see langword="null" />.</param>
/// <param name="luaState">The resolved SDK LuaState symbol, or <see langword="null" />.</param>
internal sealed class LuaBindingContractSymbols(
	INamedTypeSymbol? luaFunctionAttribute,
	INamedTypeSymbol? luaGlobalAttribute,
	INamedTypeSymbol? luaMarshallerAttribute,
	INamedTypeSymbol? luaMarshallerContract,
	INamedTypeSymbol? luaClassAttribute,
	INamedTypeSymbol? luaMethodAttribute,
	INamedTypeSymbol? luaPropertyAttribute,
	INamedTypeSymbol? luaState)
{
	/// <summary>The marker attribute of an exported Lua function.</summary>
	public INamedTypeSymbol? LuaFunctionAttribute
	{
		get;
	} = luaFunctionAttribute;

	/// <summary>The marker attribute of a bound Lua global.</summary>
	public INamedTypeSymbol? LuaGlobalAttribute
	{
		get;
	} = luaGlobalAttribute;

	/// <summary>The marker attribute that names a concrete static marshaller.</summary>
	public INamedTypeSymbol? LuaMarshallerAttribute
	{
		get;
	} = luaMarshallerAttribute;

	/// <summary>The static-abstract marshaller contract.</summary>
	public INamedTypeSymbol? LuaMarshallerContract
	{
		get;
	} = luaMarshallerContract;

	/// <summary>The marker attribute of a generated borrowed Lua object-handle struct.</summary>
	public INamedTypeSymbol? LuaClassAttribute
	{
		get;
	} = luaClassAttribute;

	/// <summary>The marker attribute of a generated Lua object method body.</summary>
	public INamedTypeSymbol? LuaMethodAttribute
	{
		get;
	} = luaMethodAttribute;

	/// <summary>The marker attribute of generated Lua object property accessors.</summary>
	public INamedTypeSymbol? LuaPropertyAttribute
	{
		get;
	} = luaPropertyAttribute;

	/// <summary>The real SDK LuaState symbol, prohibited as a LuaMethod parameter.</summary>
	public INamedTypeSymbol? LuaState
	{
		get;
	} = luaState;
}
