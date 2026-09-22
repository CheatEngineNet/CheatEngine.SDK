using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The generator is silent (no file, no diagnostic, no exception) whenever there is not exactly one valid plugin:
///     explaining why is the analyzers' job (CESDK0001 and CESDK0002).
/// </summary>
public sealed class NoOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string Usings = "using CheatEngine.SDK.Annotations.Plugin; using CheatEngine.SDK.Hosting.Plugin;\n";

	private const string Body = "{ protected override void OnEnable() { } protected override void OnDisable() { } }";

	public static TheoryData<string, string> InvalidShapes
	{
		get
		{
			TheoryData<string, string> data = new();
			foreach ((string shape, string declaration) in ClassShapeRejections())
			{
				data.Add(shape, declaration);
			}

			foreach ((string shape, string declaration) in AccessibilityAndBaseRejections())
			{
				data.Add(shape, declaration);
			}

			foreach ((string shape, string declaration) in ConstructorRejections())
			{
				data.Add(shape, declaration);
			}

			foreach ((string shape, string declaration) in NameAndEntryPointRejections())
			{
				data.Add(shape, declaration);
			}

			return data;
		}
	}

	private static IEnumerable<(string Shape, string Declaration)> ClassShapeRejections()
	{
		yield return ("abstract class",
			$"[CheatEnginePlugin(\"P\")] public abstract class P : CheatEnginePlugin {Body}");
		yield return ("static class", "[CheatEnginePlugin(\"P\")] public static class P { }");
		yield return ("generic class",
			$"[CheatEnginePlugin(\"P\")] public sealed class P<T> : CheatEnginePlugin {Body}");
		yield return (
			"nested in a generic class",
			$"public static class Outer<T> {{ [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body} }}");
		yield return ("struct", "[CheatEnginePlugin(\"P\")] public struct P { }");
		yield return ("record class", "[CheatEnginePlugin(\"P\")] public sealed record P : CheatEnginePlugin;");
	}

	private static IEnumerable<(string Shape, string Declaration)> AccessibilityAndBaseRejections()
	{
		yield return ("not derived from the plugin base", "[CheatEnginePlugin(\"P\")] public sealed class P { }");
		yield return (
			"derived from a look-alike base",
			"namespace Other.Hosting { public abstract class CheatEnginePlugin { } } [CheatEnginePlugin(\"P\")] public sealed class P : Other.Hosting.CheatEnginePlugin { }");
		// The SDK namespace is two segments deep: a base that lacks the SDK segment, or that sits under another root,
		// is not the plugin base either.
		yield return (
			"derived from a look-alike base without the SDK segment",
			"namespace CheatEngine.Hosting.Plugin { public abstract class CheatEnginePlugin { } } [CheatEnginePlugin(\"P\")] public sealed class P : CheatEngine.Hosting.Plugin.CheatEnginePlugin { }");
		yield return (
			"derived from a look-alike base under another root namespace",
			"namespace Other.CheatEngine.SDK.Hosting.Plugin { public abstract class CheatEnginePlugin { } } [CheatEnginePlugin(\"P\")] public sealed class P : Other.CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin { }");
		yield return (
			"private nested class",
			$"public static class Outer {{ [CheatEnginePlugin(\"P\")] private sealed class P : CheatEnginePlugin {Body} }}");
		yield return (
			"protected nested class",
			$"public class Outer {{ [CheatEnginePlugin(\"P\")] protected sealed class P : CheatEnginePlugin {Body} }}");
		yield return (
			"nested in a private class",
			$"public static class Outer {{ private static class Hidden {{ [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body} }} }}");
		yield return ("file-local class", $"[CheatEnginePlugin(\"P\")] file sealed class P : CheatEnginePlugin {Body}");
	}

	// The last three cases are rejected by the shape predicate that the generator shares with CESDK0001: emitting
	// would make the compiler fail the generated file with CS9035 / CS0619.
	private static IEnumerable<(string Shape, string Declaration)> ConstructorRejections()
	{
		yield return (
			"no parameterless constructor",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P(int value) {{ _ = value; }} {Body[1..]}");
		yield return (
			"optional-only constructor is not a parameterless contract",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P(int value = 0) {{ _ = value; }} {Body[1..]}");
		yield return (
			"params-only constructor is not a parameterless contract",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P(params int[] values) {{ _ = values; }} {Body[1..]}");
		yield return (
			"private constructor",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ private P() {{ }} {Body[1..]}");
		yield return (
			"protected constructor",
			$"[CheatEnginePlugin(\"P\")] public class P : CheatEnginePlugin {{ protected P() {{ }} {Body[1..]}");
		yield return (
			"required member without a constructor that sets it",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public required int Value {{ get; init; }} {Body[1..]}");
		yield return (
			"optional constructor that sets required members is not a parameterless contract",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ [System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public P(int value = 0) {{ Value = value; }} public required int Value {{ get; init; }} {Body[1..]}");
		yield return (
			"obsolete as error on the class",
			$"[System.Obsolete(\"no\", true)] [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body}");
		yield return (
			"obsolete as error on the constructor",
			$"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ [System.Obsolete(\"no\", true)] public P() {{ }} {Body[1..]}");
	}

	// The host imposes the name CESDK.CESDK on the generated entry point: a plugin that takes it (or lives inside a
	// type that does) leaves no room for it. Emitting anyway gives CS0101 plus misleading errors in the author's own
	// file (the last two entries).
	private static IEnumerable<(string Shape, string Declaration)> NameAndEntryPointRejections()
	{
		yield return ("empty name", $"[CheatEnginePlugin(\"\")] public sealed class P : CheatEnginePlugin {Body}");
		yield return (
			"white-space name",
			$"[CheatEnginePlugin(\" \\t\\u00A0\")] public sealed class P : CheatEnginePlugin {Body}");
		yield return ("null name", $"[CheatEnginePlugin(null!)] public sealed class P : CheatEnginePlugin {Body}");
		yield return ("missing name argument", $"[CheatEnginePlugin] public sealed class P : CheatEnginePlugin {Body}");
		yield return (
			"named like the entry point",
			$"namespace CESDK {{ [CheatEnginePlugin(\"P\")] public sealed class CESDK : CheatEnginePlugin {Body} }}");
		yield return (
			"nested in a type named like the entry point",
			$"namespace CESDK {{ public static class CESDK {{ [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body} }} }}");
	}

	[Fact]
	public void Generator_no_plugin_class_emits_nothing()
	{
		GeneratorRun run = roslyn.Run("namespace Demo; public sealed class NotAPlugin { }");

		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_plugin_base_without_the_attribute_emits_nothing()
	{
		// Discovery is attribute-driven: deriving from the base class alone is not a plugin declaration.
		GeneratorRun run = roslyn.Run($"{Usings} public sealed class P : CheatEnginePlugin {Body}");

		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_user_declared_host_entry_point_type_emits_nothing()
	{
		// The user type is not a plugin class, so PluginShape cannot see it. Emitting a second CESDK.CESDK would be a
		// duplicate type error in generated code; CESDK0005 is the analyzer's source-local explanation.
		GeneratorRun run = roslyn.Run(
			PluginSources.Nominal,
			"namespace CESDK { public static class CESDK { } }");

		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_attribute_with_the_same_simple_name_from_another_namespace_emits_nothing()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                namespace Other
		                                {
		                                    [System.AttributeUsage(System.AttributeTargets.Class)]
		                                    public sealed class CheatEnginePluginAttribute(string name) : System.Attribute
		                                    {
		                                        public string Name { get; } = name;
		                                    }
		                                }

		                                [Other.CheatEnginePlugin("P")]
		                                public sealed class P : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin {{Body}}
		                                """);

		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_two_valid_plugins_emits_nothing()
	{
		GeneratorRun run = roslyn.Run(
			PluginSources.WithNameExpression("\"First\"", "FirstPlugin"),
			PluginSources.WithNameExpression("\"Second\"", "SecondPlugin"));

		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_two_valid_plugins_in_one_file_emits_nothing()
	{
		GeneratorRun run = roslyn.Run(
			$"{Usings} [CheatEnginePlugin(\"A\")] public sealed class A : CheatEnginePlugin {Body} [CheatEnginePlugin(\"B\")] public sealed class B : CheatEnginePlugin {Body}");

		run.AssertNoOutput();
	}

	[Theory]
	[MemberData(nameof(InvalidShapes))]
	public void Generator_invalid_plugin_shape_emits_nothing(string shape, string declaration)
	{
		GeneratorRun run = roslyn.Run(Usings + declaration);

		Assert.True(run.GeneratedSources.IsEmpty, $"Unexpected output for: {shape}");
		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_one_valid_and_one_invalid_plugin_emits_for_the_valid_one()
	{
		// "Exactly one VALID plugin": the abstract class is CESDK0001's business and does not make the valid
		// class ambiguous.
		GeneratorRun run = roslyn.Run(
			PluginSources.Nominal,
			$"{Usings} [CheatEnginePlugin(\"Broken\")] public abstract class Broken : CheatEnginePlugin {{ }}");

		Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Theory]
	[InlineData("false")]
	[InlineData("False")]
	[InlineData("FALSE")]
	[InlineData("  false ")]
	public void Generator_build_property_false_emits_nothing(string value)
	{
		GeneratorRun run = RoslynFixture.Run(
			roslyn.CreateCompilation(PluginSources.Nominal),
			TestAnalyzerConfigOptionsProvider.WithBuildProperty("CheatEngineSdkGenerateEntryPoint", value));

		run.AssertNoOutput();
	}

	[Theory]
	[InlineData("true")]
	[InlineData("True")]
	public void Generator_build_property_true_emits(string value)
	{
		GeneratorRun run = RoslynFixture.Run(
			roslyn.CreateCompilation(PluginSources.Nominal),
			TestAnalyzerConfigOptionsProvider.WithBuildProperty("CheatEngineSdkGenerateEntryPoint", value));

		Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), run.SingleGeneratedText);
	}

	[Fact]
	public void Generator_missing_direct_package_property_emits_nothing()
	{
		GeneratorRun run = RoslynFixture.Run(
			roslyn.CreateCompilation(PluginSources.Nominal),
			TestAnalyzerConfigOptionsProvider.Empty);

		run.AssertNoOutput();
	}
}
