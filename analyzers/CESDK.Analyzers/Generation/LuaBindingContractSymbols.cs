using Microsoft.CodeAnalysis;

namespace CESDK.Analyzers.Generation;

/// <summary>
///     The well-known attribute types behind the CESDK2xxx rules, resolved once per compilation in the
///     compilation-start action of <see cref="LuaBindingAnalyzer" />. Immutable, safe for concurrent use. Either symbol
///     may be missing on its own (a project can reference <c>CESDK.Annotations</c> without ever declaring the other
///     kind of binding); the analyzer registers as long as at least one resolves.
/// </summary>
/// <param name="luaFunctionAttribute">
///     The resolved <c>CESDK.Annotations.Lua.LuaFunctionAttribute</c>, or
///     <see langword="null" />.
/// </param>
/// <param name="luaGlobalAttribute">
///     The resolved <c>CESDK.Annotations.Lua.LuaGlobalAttribute</c>, or
///     <see langword="null" />.
/// </param>
internal sealed class LuaBindingContractSymbols(
    INamedTypeSymbol? luaFunctionAttribute,
    INamedTypeSymbol? luaGlobalAttribute)
{
    /// <summary>The marker attribute of an exported Lua function.</summary>
    public INamedTypeSymbol? LuaFunctionAttribute { get; } = luaFunctionAttribute;

    /// <summary>The marker attribute of a bound Lua global.</summary>
    public INamedTypeSymbol? LuaGlobalAttribute { get; } = luaGlobalAttribute;
}
