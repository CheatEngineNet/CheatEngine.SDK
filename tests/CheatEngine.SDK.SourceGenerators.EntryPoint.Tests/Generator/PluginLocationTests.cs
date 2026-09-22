using System.Text;

using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>Where the plugin class lives decides only one thing: the <c>global::</c> name in <c>Create()</c>.</summary>
public sealed class PluginLocationTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	[Fact]
	public void Generator_plugin_in_nested_namespace_uses_fully_qualified_name()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                namespace Company.Product.Plugins.Trainer
		                                {
		                                    [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Trainer")]
		                                    public sealed class TrainerPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::Company.Product.Plugins.Trainer.TrainerPlugin", "\"Trainer\"u8"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_plugin_in_global_namespace_uses_global_alias_only()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Global")]
		                                public sealed class GlobalPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                {
		                                    {{PluginSources.LifecycleOverrides}}
		                                }
		                                """);

		Assert.Equal(ExpectedBootstrap.Text("global::GlobalPlugin", "\"Global\"u8"), run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_plugin_nested_in_another_class_uses_containing_type_chain()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                namespace Demo
		                                {
		                                    public static class Outer
		                                    {
		                                        internal static class Middle
		                                        {
		                                            [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Nested")]
		                                            internal sealed class NestedPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                            {
		                                                {{PluginSources.LifecycleOverrides}}
		                                            }
		                                        }
		                                    }
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::Demo.Outer.Middle.NestedPlugin", "\"Nested\"u8"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_partial_plugin_across_two_files_merges_the_declarations()
	{
		// The attribute is on one part; the base class, the constructor and the overrides are on the other.
		const string AttributedPart = """
		                              namespace Demo;

		                              [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Split")]
		                              public sealed partial class SplitPlugin
		                              {
		                              }
		                              """;
		const string OtherPart = $$"""
		                           namespace Demo;

		                           public sealed partial class SplitPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                           {
		                               public SplitPlugin()
		                               {
		                               }

		                               {{PluginSources.LifecycleOverrides}}
		                           }
		                           """;

		GeneratorRun run = roslyn.Run(AttributedPart, OtherPart);

		Assert.Equal(ExpectedBootstrap.Text("global::Demo.SplitPlugin", "\"Split\"u8"), run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_plugin_named_like_a_keyword_escapes_the_identifier()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                namespace @namespace
		                                {
		                                    [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Keyword")]
		                                    public sealed class @class : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(ExpectedBootstrap.Text("global::@namespace.@class", "\"Keyword\"u8"), run.SingleGeneratedText);
		Assert.Empty(run.OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
	}

	[Fact]
	public void Generator_plugin_with_non_ascii_identifiers_is_named_as_declared()
	{
		// Only the display-name literal is escaped to ASCII. Identifiers are written the way the author declared
		// them (the generated file is UTF-8), so the output is NOT ASCII-only here, and still compiles and runs.
		GeneratorRun run = roslyn.Run($$"""
		                                namespace España.Démo
		                                {
		                                    [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("x")]
		                                    public sealed class Plügin日 : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::España.Démo.Plügin日", "\"x\"u8"),
			run.SingleGeneratedText);
		Assert.Equal(Encoding.UTF8, Assert.Single(run.GeneratedSources).SourceText.Encoding);
		run.AssertCompilesClean();

		using LoadedBootstrap bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);
		Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
		Assert.Equal("España.Démo.Plügin日", bootstrap.LastPluginTypeName);
	}

	[Fact]
	public void Generator_plugin_named_like_the_file_local_factory_gets_another_factory_name()
	{
		// 'global::CESDK.PluginFactory' written inside the generated file would bind to the generated file-local
		// type of that name (a file-local type wins the lookup in its own file, even through 'global::'), so the
		// factory steps aside. CS0029 before the fix.
		GeneratorRun run = roslyn.Run($$"""
		                                namespace CESDK
		                                {
		                                    [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Factory")]
		                                    public sealed class PluginFactory : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::CESDK.PluginFactory", "\"Factory\"u8", "GeneratedPluginFactory"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();

		using LoadedBootstrap bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);
		Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
		Assert.Equal("CESDK.PluginFactory", bootstrap.LastPluginTypeName);
	}

	[Fact]
	public void Generator_plugin_nested_in_a_type_named_like_the_factory_gets_another_factory_name()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                namespace CESDK
		                                {
		                                    public static class PluginFactory
		                                    {
		                                        [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Nested")]
		                                        public sealed class Inner : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                        {
		                                            {{PluginSources.LifecycleOverrides}}
		                                        }
		                                    }
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::CESDK.PluginFactory.Inner", "\"Nested\"u8", "GeneratedPluginFactory"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Theory]
	[InlineData("Demo", "CESDK")]
	[InlineData("CESDK.Samples", "CESDK")]
	[InlineData("Demo.CESDK", "Plugin")]
	public void Generator_plugin_that_only_resembles_the_entry_point_name_is_still_bootstrapped(string @namespace,
		string className)
	{
		// Reserved is exactly the top-level type CESDK in the namespace CESDK (see NoOutputTests), nothing wider.
		GeneratorRun run = roslyn.Run($$"""
		                                namespace {{@namespace}}
		                                {
		                                    [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Look-alike")]
		                                    public sealed class {{className}} : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(ExpectedBootstrap.Text($"global::{@namespace}.{className}", "\"Look-alike\"u8"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_plugin_whose_name_only_starts_like_the_factory_keeps_the_default_factory_name()
	{
		GeneratorRun run = roslyn.Run($$"""
		                                namespace CESDK
		                                {
		                                    [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Prefix")]
		                                    public sealed class PluginFactoryPlugin : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(ExpectedBootstrap.Text("global::CESDK.PluginFactoryPlugin", "\"Prefix\"u8"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_user_type_named_like_the_file_local_factory_does_not_collide()
	{
		// A 'file' type may share its name with a type of the same namespace declared in another file, and wins the
		// lookup inside its own file.
		GeneratorRun run = roslyn.Run(
			PluginSources.Nominal,
			"namespace CESDK { public sealed class PluginFactory { public static int Marker => 1; } }");

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();

		using LoadedBootstrap bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);
		Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
		Assert.Equal("Demo.DemoPlugin", bootstrap.LastPluginTypeName);
	}

	[Fact]
	public void Generator_plugin_inside_a_CESDK_namespace_still_compiles_thanks_to_global_names()
	{
		// Discouraged (analyzer CESDK0004) but it must not break the generated file itself.
		GeneratorRun run = roslyn.Run($$"""
		                                namespace CESDK.Samples
		                                {
		                                    [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Inside")]
		                                    public sealed class InsidePlugin : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(ExpectedBootstrap.Text("global::CESDK.Samples.InsidePlugin", "\"Inside\"u8"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_plugin_declared_under_the_sdk_own_namespace_is_bootstrapped()
	{
		// Only CESDK.CESDK is reserved. A plugin that shares the CheatEngine.SDK root with the SDK namespaces is an
		// ordinary plugin: nothing in the generated file binds a simple name to it.
		GeneratorRun run = roslyn.Run($$"""
		                                namespace CheatEngine.SDK.Samples
		                                {
		                                    [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Sdk")]
		                                    public sealed class SdkNamespacePlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                                    {
		                                        {{PluginSources.LifecycleOverrides}}
		                                    }
		                                }
		                                """);

		Assert.Equal(
			ExpectedBootstrap.Text("global::CheatEngine.SDK.Samples.SdkNamespacePlugin", "\"Sdk\"u8"),
			run.SingleGeneratedText);
		run.AssertCompilesClean();

		using LoadedBootstrap bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);
		Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
		Assert.Equal("CheatEngine.SDK.Samples.SdkNamespacePlugin", bootstrap.LastPluginTypeName);
	}
}
