using CESDK.Annotations.Lua;
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Plugin;
using CESDK.SourceGenerators.Shared;

namespace CESDK.SourceGenerators.EntryPoint.Tests.SharedCode;

/// <summary>
///     The metadata names the generators and analyzers look the attributes and the plugin base class up by
///     (<c>AnnotationsMetadataNames</c>) are the full names of the real types. A namespace change in
///     <c>CESDK.Annotations</c> or <c>CESDK.Hosting</c> that leaves a string behind still compiles, and it silently
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
