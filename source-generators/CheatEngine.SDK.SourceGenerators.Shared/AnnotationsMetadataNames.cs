using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared;

/// <summary>
///     Fully-qualified metadata names of the generator-facing <c>CheatEngine.SDK.Annotations</c> marker attributes, and of
///     the
///     <c>CheatEngine.SDK.Hosting</c> plugin base class, exactly as they are looked up through
///     <c>SyntaxValueProvider.ForAttributeWithMetadataName</c> / <c>Compilation.GetTypeByMetadataName</c>.
/// </summary>
/// <remarks>
///     The single source of truth for these strings. No Roslyn component here can reference
///     <c>CheatEngine.SDK.Annotations</c> or
///     <c>CheatEngine.SDK.Hosting</c> (they are <c>netstandard2.0</c> Roslyn components; those assemblies target
///     <c>net10.0</c>),
///     so without this file each generator and analyzer that needed one of these names would redeclare it as its own
///     private constant, and the copies could drift apart (see <c>Shapes/PluginShape.cs</c> in this project,
///     which keeps the plugin-shape rules in one place and which this file follows as a pattern). Every component that
///     needs one of these names references this project, so the names cannot drift.
/// </remarks>
/// <seealso href="Shapes/PluginShape.cs">
///     <c>PluginShape</c> recognises <see cref="CheatEnginePluginBase" />'s namespace and type name structurally (a
///     separate, split representation used for base-type-chain walking, not metadata-name lookup) rather than through
///     this dotted constant; that is a different, valid representation of the same fact, not a duplicate of this one.
/// </seealso>
[SuppressMessage(
	"Meziantou.Analyzer",
	"MA0182",
	Justification =
		"This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class AnnotationsMetadataNames
{
	/// <summary>
	///     Metadata name of <c>CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute</c>, the marker attribute of a
	///     plugin
	///     class.
	/// </summary>
	public const string CheatEnginePluginAttribute = "CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute";

	/// <summary>
	///     Metadata name of <c>CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute</c>, which exports a static method to Lua
	///     as
	///     a global C function.
	/// </summary>
	public const string LuaFunctionAttribute = "CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute";

	/// <summary>
	///     Metadata name of <c>CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute</c>, which binds a partial member to a Lua
	///     global.
	/// </summary>
	public const string LuaGlobalAttribute = "CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute";

	/// <summary>
	///     Metadata name of <c>CheatEngine.SDK.Annotations.Lua.LuaMarshallerAttribute</c>, which selects a concrete
	///     static marshaller for one generated binding value.
	/// </summary>
	public const string LuaMarshallerAttribute = "CheatEngine.SDK.Annotations.Lua.LuaMarshallerAttribute";

	/// <summary>
	///     Metadata name of <c>CheatEngine.SDK.Annotations.Lua.LuaClassAttribute</c>, which identifies a borrowed
	///     Cheat Engine object-handle wrapper.
	/// </summary>
	public const string LuaClassAttribute = "CheatEngine.SDK.Annotations.Lua.LuaClassAttribute";

	/// <summary>
	///     Metadata name of <c>CheatEngine.SDK.Annotations.Lua.LuaMethodAttribute</c>, which binds an instance partial
	///     method on a generated object-handle wrapper.
	/// </summary>
	public const string LuaMethodAttribute = "CheatEngine.SDK.Annotations.Lua.LuaMethodAttribute";

	/// <summary>
	///     Metadata name of <c>CheatEngine.SDK.Annotations.Lua.LuaPropertyAttribute</c>, which binds a partial property
	///     on a generated object-handle wrapper.
	/// </summary>
	public const string LuaPropertyAttribute = "CheatEngine.SDK.Annotations.Lua.LuaPropertyAttribute";

	/// <summary>
	///     Dotted metadata name of <c>CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin</c>, the base class of every plugin,
	///     for
	///     use with <c>GetTypeByMetadataName</c>.
	/// </summary>
	public const string CheatEnginePluginBase = "CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin";
}
