using System.Text;

using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     The explicit <c>[LuaMarshaller]</c> path: the declared type must implement the matching static contract and the
///     generated binding names that concrete type directly, without a runtime registry or reflection lookup.
/// </summary>
public sealed class LuaMarshallerOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string ValidSource = """
	                                   using CheatEngine.SDK.Annotations.Lua;
	                                   using CheatEngine.SDK.Lua.Marshalling;
	                                   using CheatEngine.SDK.Lua.State;

	                                   namespace Demo;

	                                   public readonly record struct Token(long Value);

	                                   public readonly struct TokenMarshaller : ILuaMarshaller<Token>
	                                   {
	                                       public static void Push(LuaState state, Token value) => state.PushInteger(value.Value);
	                                       public static bool TryRead(LuaState state, int index, out Token value)
	                                       {
	                                           if (state.TryReadInteger(index, out var raw))
	                                           {
	                                               value = new Token(raw);
	                                               return true;
	                                           }

	                                           value = default;
	                                           return false;
	                                       }
	                                   }

	                                   public static partial class Bindings
	                                   {
	                                       [LuaFunction("twice")]
	                                       [return: LuaMarshaller(typeof(TokenMarshaller))]
	                                       public static Token Twice([LuaMarshaller(typeof(TokenMarshaller))] Token value) => new(value.Value * 2);

	                                       [LuaGlobal("writeToken")]
	                                       public static partial void Write([LuaMarshaller(typeof(TokenMarshaller))] Token value);

	                                       [LuaGlobal("readToken")]
	                                       [return: LuaMarshaller(typeof(TokenMarshaller))]
	                                       public static partial Token Read();

	                                       [LuaGlobal("readToken")]
	                                       public static partial bool TryRead([LuaMarshaller(typeof(TokenMarshaller))] out Token value);
	                                   }
	                                   """;

	[Fact]
	public void Generator_valid_explicit_marshallers_emit_direct_static_calls_for_function_and_global_values()
	{
		GeneratorRun run = roslyn.Run(ValidSource);

		run.AssertCompilesClean();
		StringBuilder textBuilder = new();
		foreach (GeneratedSourceResult source in run.GeneratedSources)
		{
			textBuilder.Append(source.SourceText);
		}

		string text = textBuilder.ToString();
		Assert.Contains("global::Demo.TokenMarshaller.TryRead(__L, 1, out global::Demo.Token __arg0)", text,
			StringComparison.Ordinal);
		Assert.Contains("global::Demo.TokenMarshaller.Push(__L, __result);", text, StringComparison.Ordinal);
		Assert.Contains("global::Demo.TokenMarshaller.Push(__L, value);", text, StringComparison.Ordinal);
		Assert.Contains("global::Demo.TokenMarshaller.TryRead(__L, -1, out global::Demo.Token __result)", text,
			StringComparison.Ordinal);
		Assert.Contains("global::Demo.TokenMarshaller.TryRead(__L, -1, out value)", text,
			StringComparison.Ordinal);
		Assert.DoesNotContain("GetType", text, StringComparison.Ordinal);
		Assert.DoesNotContain("Activator", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_marshaller_for_a_different_value_type_does_not_generate_a_binding()
	{
		const string source = """
		                      using CheatEngine.SDK.Annotations.Lua;
		                      using CheatEngine.SDK.Lua.Marshalling;
		                      using CheatEngine.SDK.Lua.State;
		                      namespace Demo;
		                      public readonly struct Token { }
		                      public readonly struct WrongMarshaller : ILuaMarshaller<int>
		                      {
		                          public static void Push(LuaState state, int value) => state.PushInteger(value);
		                          public static bool TryRead(LuaState state, int index, out int value) => state.TryReadInteger(index, out value);
		                      }
		                      public static partial class Bindings
		                      {
		                          [LuaFunction("wrong")]
		                          public static Token Wrong([LuaMarshaller(typeof(WrongMarshaller))] Token value) => value;
		                      }
		                      """;

		GeneratorRun run = roslyn.Run(source);

		Assert.Empty(run.GeneratedSources);
	}

	[Fact]
	public void Generator_non_marshaller_type_does_not_generate_a_global_wrapper()
	{
		const string source = """
		                      using CheatEngine.SDK.Annotations.Lua;
		                      namespace Demo;
		                      public readonly struct Token { }
		                      public sealed class NotAMarshaller { }
		                      public static partial class Bindings
		                      {
		                          [LuaGlobal("token")]
		                          [return: LuaMarshaller(typeof(NotAMarshaller))]
		                          public static partial Token Read();
		                      }
		                      """;

		GeneratorRun run = roslyn.Run(source);

		Assert.Empty(run.GeneratedSources);
	}

	[Fact]
	public void Generator_explicit_static_interface_marshaller_members_do_not_generate_a_binding()
	{
		const string source = """
		                      using CheatEngine.SDK.Annotations.Lua;
		                      using CheatEngine.SDK.Lua.Marshalling;
		                      using CheatEngine.SDK.Lua.State;

		                      namespace Demo;

		                      public readonly struct Token { }

		                      public readonly struct ExplicitMarshaller : ILuaMarshaller<Token>
		                      {
		                          static void ILuaMarshaller<Token>.Push(LuaState state, Token value) { }

		                          static bool ILuaMarshaller<Token>.TryRead(LuaState state, int index, out Token value)
		                          {
		                              value = default;
		                              return false;
		                          }
		                      }

		                      public static partial class Bindings
		                      {
		                          [LuaFunction("token")]
		                          public static int RoundTrip([LuaMarshaller(typeof(ExplicitMarshaller))] Token value) => 0;
		                      }
		                      """;

		roslyn.Run(source).AssertNoOutput();
	}

	[Fact]
	public void Generator_marshaller_with_direct_members_of_the_wrong_shape_does_not_generate_a_binding()
	{
		const string source = """
		                      using CheatEngine.SDK.Annotations.Lua;
		                      using CheatEngine.SDK.Lua.Marshalling;
		                      using CheatEngine.SDK.Lua.State;

		                      namespace Demo;

		                      public readonly struct Token { }

		                      public readonly struct InvalidDirectMarshaller : ILuaMarshaller<Token>
		                      {
		                          static void ILuaMarshaller<Token>.Push(LuaState state, Token value) { }

		                          static bool ILuaMarshaller<Token>.TryRead(LuaState state, int index, out Token value)
		                          {
		                              value = default;
		                              return false;
		                          }

		                          public static void Push(LuaState state, int value) { }

		                          public static bool TryRead(LuaState state, int index, out int value)
		                          {
		                              value = default;
		                              return false;
		                          }
		                      }

		                      public static partial class Bindings
		                      {
		                          [LuaGlobal("token")]
		                          public static partial bool TryRead([LuaMarshaller(typeof(InvalidDirectMarshaller))] out Token value);
		                      }
		                      """;

		roslyn.Run(source).AssertNoOutput();
	}
}
