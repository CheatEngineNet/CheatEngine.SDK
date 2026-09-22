using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     Shapes that are unusual but constructible from the generated file: they must produce output, and that output
///     must compile (a wrong "valid" verdict would surface here as a compiler error in the generated code).
/// </summary>
public sealed class ValidShapeTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string Usings = "using CheatEngine.SDK.Annotations.Plugin; using CheatEngine.SDK.Hosting.Plugin;\n";

	private const string Members = "protected override void OnEnable() { } protected override void OnDisable() { }";

	public static TheoryData<string, string> ValidShapes => new()
	{
		{
			"implicit parameterless constructor",
			$"[CheatEnginePlugin(\"P\")] internal sealed class P : CheatEnginePlugin {{ {Members} }}"
		},
		{ "unsealed class", $"[CheatEnginePlugin(\"P\")] public class P : CheatEnginePlugin {{ {Members} }}" },
		{
			"internal constructor",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ internal P() {{ }} {Members} }}"
		},
		{
			"protected internal constructor",
			$"[CheatEnginePlugin(\"P\")] public class P : CheatEnginePlugin {{ protected internal P() {{ }} {Members} }}"
		},
		{
			"extra constructors",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P() {{ }} public P(int value) {{ _ = value; }} {Members} }}"
		},
		{
			"indirect derivation",
			$"public abstract class Base : CheatEnginePlugin {{ {Members} }} [CheatEnginePlugin(\"P\")] public sealed class P : Base {{ }}"
		},
		{
			"attribute suffix spelled out",
			$"[CheatEnginePluginAttribute(\"P\")] public sealed class P : CheatEnginePlugin {{ {Members} }}"
		},
		{
			"named argument",
			$"[CheatEnginePlugin(name: \"P\")] public sealed class P : CheatEnginePlugin {{ {Members} }}"
		},
		{
			"constant expression",
			$"[CheatEnginePlugin(P.Prefix + \"\")] public sealed class P : CheatEnginePlugin {{ public const string Prefix = \"P\"; {Members} }}"
		},
		{
			"attribute among others",
			$"[System.Serializable, CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ {Members} }}"
		}
	};

	public static TheoryData<string, string, string> DeclaredDiagnosticIds => new()
	{
		{
			"experimental class",
			$"[CheatEnginePlugin(\"P\")] [System.Diagnostics.CodeAnalysis.Experimental(\"EXP001\")] public sealed class P : CheatEnginePlugin {{ {Members} }}",
			"EXP001"
		},
		{
			"experimental constructor",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ [System.Diagnostics.CodeAnalysis.Experimental(\"EXP002\")] public P() {{ }} {Members} }}",
			"EXP002"
		},
		{
			"obsolete class with a custom diagnostic id",
			$"[CheatEnginePlugin(\"P\")] [System.Obsolete(\"Old.\", DiagnosticId = \"MY0001\")] public sealed class P : CheatEnginePlugin {{ {Members} }}",
			"MY0001"
		},
		{
			"obsolete constructor with a custom diagnostic id",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ [System.Obsolete(DiagnosticId = \"MY0002\")] public P() {{ }} {Members} }}",
			"MY0002"
		},
		{
			"class and constructor, same id twice and another one",
			$"[CheatEnginePlugin(\"P\")] [System.Diagnostics.CodeAnalysis.Experimental(\"EXP001\")] public sealed class P : CheatEnginePlugin {{ [System.Diagnostics.CodeAnalysis.Experimental(\"EXP001\")] [System.Obsolete(DiagnosticId = \"MY0001\")] public P() {{ }} {Members} }}",
			"EXP001, MY0001"
		}
	};

	[Theory]
	[MemberData(nameof(ValidShapes))]
	public void Generator_constructible_plugin_shape_emits_compiling_bootstrap(string shape, string declaration)
	{
		GeneratorRun run = roslyn.Run(Usings + declaration);

		Assert.True(run.GeneratedSources.Length == 1, $"No output for: {shape}");
		Assert.Equal(ExpectedBootstrap.Text("global::P", "\"P\"u8"), run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_obsolete_plugin_class_does_not_warn_in_generated_code()
	{
		GeneratorRun run = roslyn.Run(
			$"{Usings} [CheatEnginePlugin(\"P\")] [System.Obsolete(\"Use the new plugin.\")] public sealed class P : CheatEnginePlugin {{ {Members} }}");

		Assert.Equal(ExpectedBootstrap.Text("global::P", "\"P\"u8"), run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_required_members_set_by_the_parameterless_constructor_compile()
	{
		// The constructible half of the 'required' story; the other half is in KnownLimitationTests.
		GeneratorRun run = roslyn.Run($$"""
		                                {{Usings}}
		                                [CheatEnginePlugin("P")]
		                                public sealed class P : CheatEnginePlugin
		                                {
		                                    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
		                                    public P() => Value = 1;

		                                    public required int Value { get; init; }

		                                    {{Members}}
		                                }
		                                """);

		Assert.Equal(ExpectedBootstrap.Text("global::P", "\"P\"u8"), run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_pragmas_follow_the_selected_real_parameterless_constructor_only()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                {{Usings}}
		                                [CheatEnginePlugin("P")]
		                                public sealed class P : CheatEnginePlugin
		                                {
		                                    [System.Diagnostics.CodeAnalysis.Experimental("OPTIONAL01")]
		                                    public P(int ignored = 0) => _ = ignored;

		                                    [System.Diagnostics.CodeAnalysis.Experimental("PARAMETERLESS01")]
		                                    public P() { }

		                                    {{Members}}
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::P", "\"P\"u8", declaredDiagnosticIds: "PARAMETERLESS01"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Theory]
	[MemberData(nameof(DeclaredDiagnosticIds))]
	public void Generator_diagnostic_ids_declared_by_the_plugin_class_are_disabled_in_generated_code(string shape,
		string declaration, string expectedIds)
	{
		// [Experimental] is an error by default and [Obsolete(DiagnosticId = ...)] is not CS0612/CS0618: both would
		// land in a file the author cannot edit. A pragma does suppress them (they are warnings-as-errors).
		GeneratorRun run = roslyn.Run(Usings + declaration);

		Assert.True(run.GeneratedSources.Length == 1, $"No output for: {shape}");
		Assert.Equal(ExpectedBootstrap.Text("global::P", "\"P\"u8", declaredDiagnosticIds: expectedIds),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_experimental_containing_type_is_disabled_too()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                {{Usings}}
		                                [System.Diagnostics.CodeAnalysis.Experimental("OUTER01")]
		                                public static class Outer
		                                {
		                                    [CheatEnginePlugin("P")]
		                                    [System.Diagnostics.CodeAnalysis.Experimental("INNER01")]
		                                    public sealed class P : CheatEnginePlugin { {{Members}} }
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::Outer.P", "\"P\"u8", declaredDiagnosticIds: "OUTER01, INNER01"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_attribute_through_a_using_alias_is_recognised()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                using Plugin = CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute;

		                                [Plugin("Aliased")]
		                                public sealed class P : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin { {{Members}} }
		                                """);

		Assert.Equal(ExpectedBootstrap.Text("global::P", "\"Aliased\"u8"), run.SingleGeneratedText);
		run.AssertCompilesClean();
	}
}
