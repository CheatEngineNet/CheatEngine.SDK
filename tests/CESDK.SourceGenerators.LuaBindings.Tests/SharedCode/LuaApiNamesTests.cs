using System.Collections.Immutable;
using System.Reflection;
using CESDK.Lua.State;
using CESDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CESDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CESDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CESDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>
///     What the generators write into generated code (<c>LuaApiNames</c>) and what the parsers recognise in symbols
///     (<c>LuaValueKindMapper.IsLuaState</c>) denotes the real types of <c>CESDK.Lua</c>. A namespace change in
///     <c>CESDK.Lua</c> that leaves a string behind still compiles here, and only fails in a consumer, so it has to
///     fail in this test.
/// </summary>
public sealed class LuaApiNamesTests
{
    private static readonly Assembly LuaAssembly = typeof(LuaState).Assembly;

    [Fact]
    public void Every_CESDK_name_written_into_generated_code_denotes_a_real_member_of_the_lua_assembly()
    {
        var names = typeof(LuaApiNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.StartsWith("global::CESDK.", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            var text = name["global::".Length..];
            if (text.EndsWith("()", StringComparison.Ordinal))
            {
                var dot = text.LastIndexOf('.');
                var owner = LuaAssembly.GetType(text[..dot])
                            ?? throw new InvalidOperationException($"{name}: no such type in CESDK.Lua.");
                Assert.NotNull(owner.GetMethod(text[(dot + 1)..^2]));
            }
            else
            {
                _ = LuaAssembly.GetType(text)
                    ?? throw new InvalidOperationException($"{name}: no such type in CESDK.Lua.");
            }
        }
    }

    [Fact]
    public void The_lua_state_the_parsers_recognise_is_the_real_LuaState()
    {
        ImmutableArray<MetadataReference> references =
        [
            .. LocalFrameworkReferences.Load(),
            MetadataReference.CreateFromFile(LuaAssembly.Location)
        ];
        var compilation = CSharpCompilation.Create("RealLuaState", references: references);

        var luaState = compilation.GetTypeByMetadataName(typeof(LuaState).FullName!);

        Assert.NotNull(luaState);
        Assert.True(LuaValueKindMapper.IsLuaState(luaState));
        Assert.False(LuaValueKindMapper.IsLuaState(compilation.GetSpecialType(SpecialType.System_Int32)));
    }
}
