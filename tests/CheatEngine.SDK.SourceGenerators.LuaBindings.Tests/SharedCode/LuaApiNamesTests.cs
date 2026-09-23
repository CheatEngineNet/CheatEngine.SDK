using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>
///     What the generators write into generated code (<c>LuaApiNames</c>) and what the parsers recognise in symbols
///     (<c>LuaValueKindMapper.IsLuaState</c>) denotes the real types of <c>CheatEngine.SDK.Lua</c>. A namespace change in
///     <c>CheatEngine.SDK.Lua</c> that leaves a string behind still compiles here, and only fails in a consumer, so it has
///     to
///     fail in this test.
/// </summary>
public sealed class LuaApiNamesTests
{
	private static readonly Assembly LuaAssembly = typeof(LuaState).Assembly;
	private static readonly Assembly EngineAssembly = typeof(CEObject).Assembly;

	[Fact]
	public void Every_CheatEngine_SDK_name_written_into_generated_code_denotes_a_real_member_of_the_lua_assembly()
	{
		string[] names = typeof(LuaApiNames)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
			.Select(field => (string) field.GetRawConstantValue()!)
			.Where(value => value.StartsWith("global::CheatEngine.SDK.", StringComparison.Ordinal))
			.ToArray();

		Assert.NotEmpty(names);
		foreach (string name in names)
		{
			string text = name["global::".Length..];
			if (text.EndsWith("()", StringComparison.Ordinal))
			{
				int dot = text.LastIndexOf('.');
				Type owner = FindType(text[..dot])
							 ?? throw new InvalidOperationException($"{name}: no such type in the SDK assemblies.");
				Assert.NotEmpty(owner.GetMember(
					text[(dot + 1)..^2],
					MemberTypes.Method,
					BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
			}
			else
			{
				_ = FindType(text)
					?? throw new InvalidOperationException($"{name}: no such type in the SDK assemblies.");
			}
		}
	}

	private static Type? FindType(string fullName)
	{
		return LuaAssembly.GetType(fullName) ?? LuaAssembly.GetType(fullName + "`1")
			?? EngineAssembly.GetType(fullName) ?? EngineAssembly.GetType(fullName + "`1");
	}

	[Fact]
	public void The_lua_state_the_parsers_recognise_is_the_real_LuaState()
	{
		ImmutableArray<MetadataReference> references =
		[
			.. LocalFrameworkReferences.Load(),
			MetadataReference.CreateFromFile(LuaAssembly.Location)
		];
		CSharpCompilation compilation = CSharpCompilation.Create("RealLuaState", references: references);

		INamedTypeSymbol? luaState = compilation.GetTypeByMetadataName(typeof(LuaState).FullName!);

		Assert.NotNull(luaState);
		Assert.True(LuaValueKindMapper.IsLuaState(luaState, luaState));
		Assert.False(LuaValueKindMapper.IsLuaState(compilation.GetSpecialType(SpecialType.System_Int32), luaState));
	}

	// The SDK namespaces are two segments deep (CheatEngine.SDK): a walk that stops early, starts late or forgets to end
	// at the global namespace would take one of these look-alikes for the real LuaState.
	[Theory]
	[InlineData("CheatEngine.Lua.State")]
	[InlineData("SDK.Lua.State")]
	[InlineData("Lua.State")]
	[InlineData("Other.CheatEngine.SDK.Lua.State")]
	[InlineData("CheatEngine.SDK.State")]
	public void A_look_alike_LuaState_outside_the_sdk_namespace_is_not_recognised(string @namespace)
	{
		CSharpCompilation compilation = CSharpCompilation.Create(
			"LookAlikeLuaState",
			[
				CSharpSyntaxTree.ParseText($"namespace {@namespace} {{ public struct LuaState {{ }} }}",
					cancellationToken: TestContext.Current.CancellationToken)
			],
			[
				.. LocalFrameworkReferences.Load(),
				MetadataReference.CreateFromFile(LuaAssembly.Location)
			]);

		INamedTypeSymbol? lookAlike = compilation.GetTypeByMetadataName(@namespace + ".LuaState");
		INamedTypeSymbol? luaState = compilation.GetTypeByMetadataName(typeof(LuaState).FullName!);

		Assert.NotNull(lookAlike);
		Assert.NotNull(luaState);
		Assert.False(LuaValueKindMapper.IsLuaState(lookAlike, luaState));
	}
}
