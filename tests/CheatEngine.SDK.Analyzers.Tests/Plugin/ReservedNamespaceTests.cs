using CheatEngine.SDK.Analyzers.Diagnostics;

using Microsoft.CodeAnalysis.Testing;

using Verifier = CheatEngine.SDK.Analyzers.Tests.Infrastructure.AnalyzerVerifier<
	CheatEngine.SDK.Analyzers.Plugin.CheatEnginePluginAnalyzer>;

namespace CheatEngine.SDK.Analyzers.Tests.Plugin;

/// <summary>
///     CESDK0004: a plugin assembly must not declare <c>CESDK</c> or a namespace under it (compilation-end diagnostic,
///     reported on each outermost namespace declaration).
/// </summary>
public sealed class ReservedNamespaceTests
{
	private const string PluginInMyPlugin = """
	                                        using CheatEngine.SDK.Annotations.Plugin;
	                                        using CheatEngine.SDK.Hosting.Plugin;

	                                        namespace MyPlugin;

	                                        [CheatEnginePlugin("Demo")]
	                                        public sealed class DemoPlugin : CheatEnginePlugin
	                                        {
	                                            protected override void OnEnable() { }
	                                            protected override void OnDisable() { }
	                                        }
	                                        """;

	[Fact]
	public async Task File_scoped_namespace_under_cesdk_reports_on_the_name()
	{
		await Verifier.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Plugin;
			using CheatEngine.SDK.Hosting.Plugin;

			namespace {|#0:CESDK.MyPlugin|};

			[CheatEnginePlugin("Demo")]
			public sealed class DemoPlugin : CheatEnginePlugin
			{
			    protected override void OnEnable() { }
			    protected override void OnDisable() { }
			}
			""",
			Reserved(0, "CESDK.MyPlugin"));
	}

	[Fact]
	public async Task Namespace_exactly_cesdk_reports()
	{
		await Verifier.VerifyAsync(
			[
				("Plugin.cs", PluginInMyPlugin),
				("Helpers.cs", """
				               namespace {|#0:CESDK|}
				               {
				                   internal static class Helpers
				                   {
				                   }
				               }
				               """)
			],
			Reserved(0, "CESDK"));
	}

	[Fact]
	public async Task Nested_declarations_report_the_outermost_one_only()
	{
		await Verifier.VerifyAsync(
			[
				("Plugin.cs", PluginInMyPlugin),
				("Helpers.cs", """
				               namespace {|#0:CESDK.Tools|}
				               {
				                   namespace Deep.Deeper
				                   {
				                       internal static class Helpers
				                       {
				                       }
				                   }
				               }
				               """)
			],
			Reserved(0, "CESDK.Tools"));
	}

	[Fact]
	public async Task Every_declaration_in_every_file_reports()
	{
		await Verifier.VerifyAsync(
			[
				("Plugin.cs", PluginInMyPlugin),
				("First.cs", """
				             namespace {|#0:CESDK.Tools|}
				             {
				                 internal static class First
				                 {
				                 }
				             }

				             namespace {|#1:CESDK.Tools|}
				             {
				                 internal static class Second
				                 {
				                 }
				             }
				             """),
				("Second.cs", """
				              namespace {|#2:CESDK.Other.Deep|};

				              internal static class Third
				              {
				              }
				              """)
			],
			Reserved(0, "CESDK.Tools"),
			Reserved(1, "CESDK.Tools"),
			Reserved(2, "CESDK.Other.Deep"));
	}

	[Fact]
	public async Task Namespaces_that_only_look_like_cesdk_report_nothing()
	{
		await Verifier.VerifyAsync(
		[
			("Plugin.cs", PluginInMyPlugin),
			("Helpers.cs", """
			               namespace CESDKPlugin
			               {
			                   internal static class A
			                   {
			                   }
			               }

			               namespace Cesdk.Tools
			               {
			                   internal static class B
			                   {
			                   }
			               }

			               namespace MyPlugin.CESDK
			               {
			                   internal static class C
			                   {
			                   }
			               }

			               namespace MyPlugin
			               {
			                   namespace CESDK.Tools
			                   {
			                       internal static class D
			                       {
			                       }
			                   }
			               }
			               """)
		]);
	}

	[Fact]
	public async Task Namespaces_of_the_sdk_itself_are_not_reserved()
	{
		// Only the namespace of the host-mandated CESDK.CESDK type is reserved. The SDK's own root, CheatEngine.SDK, is not:
		// its simple name 'CheatEngine' never binds to the generated bootstrap type.
		await Verifier.VerifyAsync(
		[
			("Plugin.cs", PluginInMyPlugin),
			("Helpers.cs", """
			               namespace CheatEngine.SDK.Extensions
			               {
			                   internal static class Helpers
			                   {
			                   }
			               }
			               """)
		]);
	}

	[Fact]
	public async Task Assembly_without_a_plugin_class_reports_nothing()
	{
		// A class library is not a plugin assembly: it declares a namespace under CESDK and references the contract
		// types, but no class carries [CheatEnginePlugin], so the rules that are about plugins stay silent.
		await Verifier.VerifyAsync("""
		                           namespace CESDK.Tools
		                           {
		                               public abstract class Helper : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
		                               {
		                               }
		                           }
		                           """);
	}

	[Fact]
	public async Task Generated_bootstrap_namespace_reports_nothing()
	{
		await Verifier.VerifyAsync(
		[
			("Plugin.cs", PluginInMyPlugin),
			("CheatEngine.SDK.EntryPoint.g.cs", """
			                                    // <auto-generated/>
			                                    #nullable enable
			                                    namespace CESDK
			                                    {
			                                        internal static class CESDK
			                                        {
			                                            public static int CEPluginInitialize(global::System.IntPtr args, int size) => 1;
			                                        }
			                                    }
			                                    """)
		]);
	}

	[Fact]
	public async Task Simple_name_cesdk_binds_to_the_bootstrap_type_inside_the_namespace()
	{
		// The reason for the rule, as the compiler sees it: with the bootstrap type present, 'CESDK.Tools' inside
		// namespace CESDK.MyPlugin is looked up in the TYPE CESDK.CESDK, not among the namespaces this assembly declares
		// under CESDK, and no longer compiles. Both declarations sit under CESDK, so the rule reports both.
		await Verifier.VerifyAsync(
			[
				("Plugin.cs", """
				              namespace {|#0:CESDK.MyPlugin|}
				              {
				                  [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Demo")]
				                  public sealed class DemoPlugin : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
				                  {
				                      protected override void OnEnable() { }
				                      protected override void OnDisable() { }

				                      public static CESDK.{|CS0426:Tools|}.Helper? Other => null;
				                  }
				              }
				              """),
				("Helper.cs", """
				              namespace {|#1:CESDK.Tools|}
				              {
				                  public sealed class Helper
				                  {
				                  }
				              }
				              """),
				("CheatEngine.SDK.EntryPoint.g.cs", """
				                                    // <auto-generated/>
				                                    #nullable enable
				                                    namespace CESDK
				                                    {
				                                        internal static class CESDK
				                                        {
				                                        }
				                                    }
				                                    """)
			],
			Reserved(0, "CESDK.MyPlugin"),
			Reserved(1, "CESDK.Tools"));
	}

	[Fact]
	public async Task Sdk_namespaces_still_resolve_inside_the_namespace()
	{
		// What the rule does not have to protect any more: the SDK's own namespaces start with 'CheatEngine', so a fully
		// qualified SDK name inside namespace CESDK.MyPlugin compiles even with the bootstrap type present.
		await Verifier.VerifyAsync(
			[
				("Plugin.cs", """
				              namespace {|#0:CESDK.MyPlugin|}
				              {
				                  using CheatEngine.SDK.Annotations.Plugin;

				                  [CheatEnginePlugin("Demo")]
				                  public sealed class DemoPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
				                  {
				                      protected override void OnEnable() { }
				                      protected override void OnDisable() { }
				                  }
				              }
				              """),
				("CheatEngine.SDK.EntryPoint.g.cs", """
				                                    // <auto-generated/>
				                                    #nullable enable
				                                    namespace CESDK
				                                    {
				                                        internal static class CESDK
				                                        {
				                                        }
				                                    }
				                                    """)
			],
			Reserved(0, "CESDK.MyPlugin"));
	}

	[Fact]
	public async Task Using_directive_inside_the_namespace_binds_to_the_bootstrap_type_too()
	{
		await Verifier.VerifyAsync(
			[
				("Plugin.cs", """
				              namespace {|#0:CESDK.MyPlugin|}
				              {
				                  using CESDK.{|CS0426:Tools|};

				                  [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Demo")]
				                  public sealed class DemoPlugin : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
				                  {
				                      protected override void OnEnable() { }
				                      protected override void OnDisable() { }
				                  }
				              }
				              """),
				("Helper.cs", """
				              namespace {|#1:CESDK.Tools|}
				              {
				                  public sealed class Helper
				                  {
				                  }
				              }
				              """),
				("CheatEngine.SDK.EntryPoint.g.cs", """
				                                    // <auto-generated/>
				                                    #nullable enable
				                                    namespace CESDK
				                                    {
				                                        internal static class CESDK
				                                        {
				                                        }
				                                    }
				                                    """)
			],
			Reserved(0, "CESDK.MyPlugin"),
			Reserved(1, "CESDK.Tools"));
	}

	[Fact]
	public async Task Usings_above_the_namespace_and_global_qualification_keep_compiling()
	{
		// Why the breakage looks random to a plugin author: this variant of the same code compiles.
		await Verifier.VerifyAsync(
			[
				("Plugin.cs", """
				              using CheatEngine.SDK.Annotations.Plugin;
				              using CheatEngine.SDK.Hosting.Plugin;
				              using CESDK.Tools;

				              namespace {|#0:CESDK.MyPlugin|};

				              [CheatEnginePlugin("Demo")]
				              public sealed class DemoPlugin : CheatEnginePlugin
				              {
				                  protected override void OnEnable() { }
				                  protected override void OnDisable() { }

				                  public static Helper? ViaUsing => null;

				                  public static global::CESDK.Tools.Helper? Qualified => null;
				              }
				              """),
				("Helper.cs", """
				              namespace {|#1:CESDK.Tools|}
				              {
				                  public sealed class Helper
				                  {
				                  }
				              }
				              """),
				("CheatEngine.SDK.EntryPoint.g.cs", """
				                                    // <auto-generated/>
				                                    #nullable enable
				                                    namespace CESDK
				                                    {
				                                        internal static class CESDK
				                                        {
				                                        }
				                                    }
				                                    """)
			],
			Reserved(0, "CESDK.MyPlugin"),
			Reserved(1, "CESDK.Tools"));
	}

	private static DiagnosticResult Reserved(int location, string namespaceName)
	{
		return Verifier.Diagnostic(DiagnosticDescriptors.ReservedNamespace).WithLocation(location)
			.WithArguments(namespaceName);
	}
}
