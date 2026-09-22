using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.SharedCode;

/// <summary>
///     The metadata names the generators and analyzers look the attributes and the plugin base class up by
///     (<c>AnnotationsMetadataNames</c>) are the full names of the real types. A namespace change in
///     <c>CheatEngine.SDK.Annotations</c> or <c>CheatEngine.SDK.Hosting</c> that leaves a string behind still compiles,
///     and it silently
///     switches the generators and the rules off, so it has to fail here.
/// </summary>
public sealed class AnnotationsMetadataNamesTests
{
	[Fact]
	public void Plugin_names_match_the_real_types()
	{
		Assert.Equal(AnnotationsMetadataNames.CheatEnginePluginAttribute, typeof(CheatEnginePluginAttribute).FullName);
		Assert.Equal(AnnotationsMetadataNames.CheatEnginePluginBase, typeof(CheatEnginePlugin).FullName);
	}

	[Fact]
	public void Lua_binding_attribute_names_match_the_real_types()
	{
		Assert.Equal(AnnotationsMetadataNames.LuaFunctionAttribute, typeof(LuaFunctionAttribute).FullName);
		Assert.Equal(AnnotationsMetadataNames.LuaGlobalAttribute, typeof(LuaGlobalAttribute).FullName);
	}
}
