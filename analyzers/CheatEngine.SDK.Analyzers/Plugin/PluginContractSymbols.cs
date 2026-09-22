using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.Analyzers.Plugin;

/// <summary>
///     The well-known types behind the plugin rules, resolved once per compilation in the compilation-start action of
///     <see cref="CheatEnginePluginAnalyzer" />. Immutable, safe for concurrent use.
/// </summary>
/// <param name="pluginAttribute">The resolved <c>CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute</c>.</param>
/// <param name="pluginBase">The resolved <c>CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin</c>.</param>
/// <param name="setsRequiredMembersAttribute">
///     The resolved <c>System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute</c>; <see langword="null" /> when the
///     compilation has none, in which case no constructor counts as setting the required members.
/// </param>
/// <param name="obsoleteAttribute">
///     The resolved <c>System.ObsoleteAttribute</c>; <see langword="null" /> when the compilation has none, in which case
///     obsolete errors are not looked for.
/// </param>
/// <remarks>
///     Carries the resolved SDK plugin base so the shared shape predicate can use symbol identity. A source type with the
///     same namespace and name from another assembly does not satisfy the entry-point contract.
/// </remarks>
internal sealed class PluginContractSymbols(
	INamedTypeSymbol pluginAttribute,
	INamedTypeSymbol pluginBase,
	INamedTypeSymbol? setsRequiredMembersAttribute,
	INamedTypeSymbol? obsoleteAttribute)
{
	/// <summary>The marker attribute of a plugin class.</summary>
	public INamedTypeSymbol PluginAttribute
	{
		get;
	} = pluginAttribute;

	/// <summary>The actual SDK plugin base. Source lookalikes from another assembly never satisfy the entry-point contract.</summary>
	public INamedTypeSymbol PluginBase
	{
		get;
	} = pluginBase;

	/// <summary>On a constructor: <c>new T()</c> needs no object initializer although <c>T</c> has required members.</summary>
	public INamedTypeSymbol? SetsRequiredMembersAttribute
	{
		get;
	} = setsRequiredMembersAttribute;

	/// <summary>With <c>error: true</c>: naming the marked symbol is CS0619.</summary>
	public INamedTypeSymbol? ObsoleteAttribute
	{
		get;
	} = obsoleteAttribute;
}
