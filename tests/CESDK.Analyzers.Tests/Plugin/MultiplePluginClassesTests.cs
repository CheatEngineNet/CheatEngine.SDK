using CESDK.Analyzers.Diagnostics;
using CESDK.Analyzers.Plugin;
using CESDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis.Testing;
using Verifier =
    CESDK.Analyzers.Tests.Infrastructure.AnalyzerVerifier<CESDK.Analyzers.Plugin.CheatEnginePluginAnalyzer>;

namespace CESDK.Analyzers.Tests.Plugin;

/// <summary>CESDK0002: exactly one plugin class per assembly (compilation-end diagnostic, reported on each class).</summary>
public sealed class MultiplePluginClassesTests
{
    [Fact]
    public async Task One_plugin_class_next_to_ordinary_classes_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   using CESDK.Annotations.Plugin;
                                   using CESDK.Hosting.Plugin;

                                   namespace MyPlugin;

                                   [CheatEnginePlugin("Demo")]
                                   public sealed class DemoPlugin : CheatEnginePlugin
                                   {
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }

                                   public sealed class Helper : CheatEnginePlugin
                                   {
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }
                                   """);
    }

    [Fact]
    public async Task Two_plugin_classes_report_on_each()
    {
        await Verifier.VerifyAsync(
            """
            using CESDK.Annotations.Plugin;
            using CESDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("First")]
            public sealed class {|#0:FirstPlugin|} : CheatEnginePlugin
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }

            [CheatEnginePlugin("Second")]
            public sealed class {|#1:SecondPlugin|} : CheatEnginePlugin
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Multiple(0, "FirstPlugin", 2),
            Multiple(1, "SecondPlugin", 2));
    }

    [Fact]
    public async Task Three_plugin_classes_in_separate_files_report_on_each()
    {
        await Verifier.VerifyAsync(
            [
                ("First.cs", PluginIn("MyPlugin.One", "{|#0:FirstPlugin|}")),
                ("Second.cs", PluginIn("MyPlugin.Two", "{|#1:SecondPlugin|}")),
                ("Third.cs", PluginIn("MyPlugin.Three", "{|#2:ThirdPlugin|}"))
            ],
            Multiple(0, "FirstPlugin", 3),
            Multiple(1, "SecondPlugin", 3),
            Multiple(2, "ThirdPlugin", 3));
    }

    [Fact]
    public async Task Nested_and_top_level_plugin_classes_report_on_each()
    {
        await Verifier.VerifyAsync(
            """
            using CESDK.Annotations.Plugin;
            using CESDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Outer")]
            public class {|#0:OuterPlugin|} : CheatEnginePlugin
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }

                [CheatEnginePlugin("Inner")]
                public sealed class {|#1:InnerPlugin|} : CheatEnginePlugin
                {
                    protected override void OnEnable() { }
                    protected override void OnDisable() { }
                }
            }
            """,
            Multiple(0, "OuterPlugin", 2),
            Multiple(1, "OuterPlugin.InnerPlugin", 2));
    }

    [Fact]
    public async Task Partial_plugin_class_in_two_files_counts_once()
    {
        await Verifier.VerifyAsync(
        [
            ("Plugin.cs", """
                          using CESDK.Annotations.Plugin;
                          using CESDK.Hosting.Plugin;

                          namespace MyPlugin;

                          [CheatEnginePlugin("Demo")]
                          public sealed partial class DemoPlugin : CheatEnginePlugin
                          {
                              protected override void OnEnable() { }
                          }
                          """),
            ("Plugin.Disable.cs", """
                                  namespace MyPlugin;

                                  public sealed partial class DemoPlugin
                                  {
                                      protected override void OnDisable() { }
                                  }
                                  """)
        ]);
    }

    [Fact]
    public async Task Invalid_plugin_class_still_counts()
    {
        await Verifier.VerifyAsync(
            """
            using CESDK.Annotations.Plugin;
            using CESDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("First")]
            public sealed class {|#0:FirstPlugin|} : CheatEnginePlugin
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }

            [CheatEnginePlugin("Second")]
            public abstract class {|#1:SecondPlugin|} : CheatEnginePlugin
            {
            }
            """,
            Multiple(0, "FirstPlugin", 2),
            Multiple(1, "SecondPlugin", 2),
            Verifier.Diagnostic(DiagnosticDescriptors.InvalidPluginClass)
                .WithLocation(1)
                .WithArguments("SecondPlugin", PluginClassProblemText.Describe(PluginShapeIssues.Abstract)));
    }

    [Fact]
    public async Task Plugin_class_in_generated_code_is_not_counted()
    {
        await Verifier.VerifyAsync(
        [
            ("Plugin.cs", PluginIn("MyPlugin", "DemoPlugin")),
            ("Generated.g.cs", "// <auto-generated/>\n" + PluginIn("MyPlugin.Generated", "GeneratedPlugin"))
        ]);
    }

    private static string PluginIn(string namespaceName, string className)
    {
        return $$"""
                 using CESDK.Annotations.Plugin;
                 using CESDK.Hosting.Plugin;

                 namespace {{namespaceName}};

                 [CheatEnginePlugin("Demo")]
                 public sealed class {{className}} : CheatEnginePlugin
                 {
                     protected override void OnEnable() { }
                     protected override void OnDisable() { }
                 }
                 """;
    }

    private static DiagnosticResult Multiple(int location, string className, int count)
    {
        return Verifier.Diagnostic(DiagnosticDescriptors.MultiplePluginClasses).WithLocation(location)
            .WithArguments(className, count);
    }
}
