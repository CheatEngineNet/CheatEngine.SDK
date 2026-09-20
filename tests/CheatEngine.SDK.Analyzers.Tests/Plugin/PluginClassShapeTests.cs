using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Plugin;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis.Testing;
using Verifier =
    CheatEngine.SDK.Analyzers.Tests.Infrastructure.AnalyzerVerifier<
        CheatEngine.SDK.Analyzers.Plugin.CheatEnginePluginAnalyzer>;

namespace CheatEngine.SDK.Analyzers.Tests.Plugin;

/// <summary>CESDK0001: which classes the generated entry point can construct, and which it cannot.</summary>
public sealed class PluginClassShapeTests
{
    [Fact]
    public async Task Sealed_public_class_with_implicit_constructor_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   using CheatEngine.SDK.Annotations.Plugin;
                                   using CheatEngine.SDK.Hosting.Plugin;

                                   namespace MyPlugin;

                                   [CheatEnginePlugin("Demo")]
                                   public sealed class DemoPlugin : CheatEnginePlugin
                                   {
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }
                                   """);
    }

    [Fact]
    public async Task Internal_unsealed_class_with_internal_constructor_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   using CheatEngine.SDK.Annotations.Plugin;
                                   using CheatEngine.SDK.Hosting.Plugin;

                                   namespace MyPlugin;

                                   [CheatEnginePlugin("Demo")]
                                   internal class DemoPlugin : CheatEnginePlugin
                                   {
                                       internal DemoPlugin() { }
                                       public DemoPlugin(int value) { }
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }
                                   """);
    }

    [Fact]
    public async Task Class_in_the_global_namespace_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Demo")]
                                   public sealed class DemoPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
                                   {
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }
                                   """);
    }

    [Fact]
    public async Task Invalid_class_in_the_global_namespace_reports()
    {
        await Verifier.VerifyAsync(
            """
            [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|}
            {
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.NotDerivedFromPluginBase));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("protected internal")]
    public async Task Nested_class_visible_in_the_assembly_reports_nothing(string accessibility)
    {
        await Verifier.VerifyAsync($$"""
                                     using CheatEngine.SDK.Annotations.Plugin;
                                     using CheatEngine.SDK.Hosting.Plugin;

                                     namespace MyPlugin;

                                     public class Container
                                     {
                                         [CheatEnginePlugin("Demo")]
                                         {{accessibility}} sealed class DemoPlugin : CheatEnginePlugin
                                         {
                                             protected override void OnEnable() { }
                                             protected override void OnDisable() { }
                                         }
                                     }
                                     """);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("private protected")]
    public async Task Nested_class_hidden_from_the_assembly_reports_inaccessible(string accessibility)
    {
        await Verifier.VerifyAsync(
            $$"""
              using CheatEngine.SDK.Annotations.Plugin;
              using CheatEngine.SDK.Hosting.Plugin;

              namespace MyPlugin;

              public class Container
              {
                  [CheatEnginePlugin("Demo")]
                  {{accessibility}} sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
                  {
                      protected override void OnEnable() { }
                      protected override void OnDisable() { }
                  }
              }
              """,
            Problem(0, "Container.DemoPlugin", PluginShapeIssues.Inaccessible));
    }

    [Fact]
    public async Task Public_class_nested_in_a_private_class_reports_inaccessible()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            public class Outer
            {
                private class Hidden
                {
                    [CheatEnginePlugin("Demo")]
                    public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
                    {
                        protected override void OnEnable() { }
                        protected override void OnDisable() { }
                    }
                }
            }
            """,
            Problem(0, "Outer.Hidden.DemoPlugin", PluginShapeIssues.Inaccessible));
    }

    [Fact]
    public async Task Class_deriving_indirectly_from_the_plugin_base_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   using CheatEngine.SDK.Annotations.Plugin;
                                   using CheatEngine.SDK.Hosting.Plugin;

                                   namespace MyPlugin;

                                   public abstract class PluginBase : CheatEnginePlugin
                                   {
                                       protected override void OnDisable() { }
                                   }

                                   [CheatEnginePlugin("Demo")]
                                   public sealed class DemoPlugin : PluginBase
                                   {
                                       protected override void OnEnable() { }
                                   }
                                   """);
    }

    [Fact]
    public async Task Primary_constructor_without_parameters_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   using CheatEngine.SDK.Annotations.Plugin;
                                   using CheatEngine.SDK.Hosting.Plugin;

                                   namespace MyPlugin;

                                   [CheatEnginePlugin("Demo")]
                                   public sealed class DemoPlugin() : CheatEnginePlugin
                                   {
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }
                                   """);
    }

    [Fact]
    public async Task Primary_constructor_with_parameters_reports_missing_constructor()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|}(int value) : CheatEnginePlugin
            {
                public int Value { get; } = value;
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.MissingParameterlessConstructor));
    }

    [Fact]
    public async Task Only_parameterized_constructors_report_missing_constructor()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
            {
                public DemoPlugin(int value) { }
                public DemoPlugin(string text) { }
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.MissingParameterlessConstructor));
    }

    [Fact]
    public async Task Constructor_with_only_optional_parameters_reports_missing_parameterless_constructor()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
            {
                public DemoPlugin(int value = 0) { }
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.MissingParameterlessConstructor));
    }

    [Fact]
    public async Task Constructor_with_a_trailing_params_parameter_reports_missing_parameterless_constructor()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
            {
                public DemoPlugin(params int[] xs) { }
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.MissingParameterlessConstructor));
    }

    [Fact]
    public async Task Optional_and_params_constructors_do_not_supply_a_parameterless_constructor()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
            {
                private DemoPlugin(int value = 0) { }
                internal DemoPlugin(string text = "") { }
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.MissingParameterlessConstructor));
    }

    [Theory]
    [InlineData("private ")]
    [InlineData("protected ")]
    [InlineData("private protected ")]
    [InlineData("")]
    public async Task Hidden_parameterless_constructor_reports_inaccessible_constructor(string accessibility)
    {
        await Verifier.VerifyAsync(
            $$"""
              using CheatEngine.SDK.Annotations.Plugin;
              using CheatEngine.SDK.Hosting.Plugin;

              namespace MyPlugin;

              [CheatEnginePlugin("Demo")]
              public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
              {
                  {{accessibility}}DemoPlugin() { }
                  protected override void OnEnable() { }
                  protected override void OnDisable() { }
              }
              """,
            Problem(0, "DemoPlugin", PluginShapeIssues.InaccessibleParameterlessConstructor));
    }

    [Theory]
    [InlineData("public required int Value { get; set; }")]
    [InlineData("public required int Value;")]
    [InlineData("public DemoPlugin() { } public required int Value { get; init; }")]
    public async Task Required_member_that_the_constructor_does_not_set_reports_required_members(string members)
    {
        await Verifier.VerifyAsync(
            $$"""
              using CheatEngine.SDK.Annotations.Plugin;
              using CheatEngine.SDK.Hosting.Plugin;

              namespace MyPlugin;

              [CheatEnginePlugin("Demo")]
              public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
              {
                  {{members}}
                  protected override void OnEnable() { }
                  protected override void OnDisable() { }
              }
              """,
            Problem(0, "DemoPlugin", PluginShapeIssues.RequiredMembers));
    }

    [Fact]
    public async Task Required_member_of_a_base_class_reports_required_members()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            public abstract class PluginBase : CheatEnginePlugin
            {
                public required string Title { get; init; }
            }

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|} : PluginBase
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.RequiredMembers));
    }

    [Fact]
    public async Task Required_member_set_by_the_parameterless_constructor_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   using System.Diagnostics.CodeAnalysis;
                                   using CheatEngine.SDK.Annotations.Plugin;
                                   using CheatEngine.SDK.Hosting.Plugin;

                                   namespace MyPlugin;

                                   [CheatEnginePlugin("Demo")]
                                   public sealed class DemoPlugin : CheatEnginePlugin
                                   {
                                       [SetsRequiredMembers]
                                       public DemoPlugin() => Value = 1;

                                       public DemoPlugin(int value) => Value = value;

                                       public required int Value { get; set; }
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }
                                   """);
    }

    [Theory]
    [InlineData("[System.Obsolete(\"Use the host.\", error: true)]", "", "")]
    [InlineData("[System.Obsolete(\"Use the host.\", true)]", "", "")]
    [InlineData("", "[System.Obsolete(\"Use the host.\", true)]", "")]
    [InlineData("", "", "[System.Obsolete(\"Use the host.\", true)]")]
    public async Task Obsolete_error_on_anything_the_entry_point_names_reports_obsolete_error(string onConstructor,
        string onClass, string onContainer)
    {
        await Verifier.VerifyAsync(
            $$"""
              using CheatEngine.SDK.Annotations.Plugin;
              using CheatEngine.SDK.Hosting.Plugin;

              namespace MyPlugin;

              {{onContainer}}
              public class Container
              {
                  {{onClass}}
                  [CheatEnginePlugin("Demo")]
                  public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
                  {
                      {{onConstructor}}
                      public DemoPlugin() { }

                      protected override void OnEnable() { }
                      protected override void OnDisable() { }
                  }
              }
              """,
            Problem(0, "Container.DemoPlugin", PluginShapeIssues.ObsoleteError));
    }

    [Theory]
    [InlineData("[System.Obsolete]")]
    [InlineData("[System.Obsolete(\"Use the host.\")]")]
    [InlineData("[System.Obsolete(\"Use the host.\", false)]")]
    public async Task Obsolete_warning_is_left_to_the_generated_pragma_and_reports_nothing(string attribute)
    {
        await Verifier.VerifyAsync($$"""
                                     using CheatEngine.SDK.Annotations.Plugin;
                                     using CheatEngine.SDK.Hosting.Plugin;

                                     namespace MyPlugin;

                                     {{attribute}}
                                     [CheatEnginePlugin("Demo")]
                                     public sealed class DemoPlugin : CheatEnginePlugin
                                     {
                                         {{attribute}}
                                         public DemoPlugin() { }

                                         protected override void OnEnable() { }
                                         protected override void OnDisable() { }
                                     }
                                     """);
    }

    [Fact]
    public async Task Static_class_reports_static_only()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public static class {|#0:DemoPlugin|}
            {
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.Static));
    }

    [Fact]
    public async Task Abstract_class_reports_abstract_and_not_its_protected_implicit_constructor()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public abstract class {|#0:DemoPlugin|} : CheatEnginePlugin
            {
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.Abstract));
    }

    [Fact]
    public async Task Generic_class_reports_generic()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|}<T> : CheatEnginePlugin
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin<T>", PluginShapeIssues.Generic));
    }

    [Fact]
    public async Task Class_nested_in_a_generic_type_reports_nested_in_generic()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            public class Container<T>
            {
                public class Inner
                {
                    [CheatEnginePlugin("Demo")]
                    public sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
                    {
                        protected override void OnEnable() { }
                        protected override void OnDisable() { }
                    }
                }
            }
            """,
            Problem(0, "Container<T>.Inner.DemoPlugin", PluginShapeIssues.NestedInGeneric));
    }

    [Fact]
    public async Task Class_without_the_plugin_base_reports_not_derived()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;

            namespace MyPlugin;

            public class CheatEnginePlugin
            {
            }

            [CheatEnginePlugin("Demo")]
            public sealed class {|#0:DemoPlugin|} : MyPlugin.CheatEnginePlugin
            {
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.NotDerivedFromPluginBase));
    }

    [Fact]
    public async Task Record_reports_not_derived_because_a_record_cannot_inherit_a_class()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            public sealed record {|#0:DemoPlugin|};
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.NotDerivedFromPluginBase));
    }

    [Fact]
    public async Task File_local_class_reports_file_local()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;
            using CheatEngine.SDK.Hosting.Plugin;

            namespace MyPlugin;

            [CheatEnginePlugin("Demo")]
            file sealed class {|#0:DemoPlugin|} : CheatEnginePlugin
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.FileLocal));
    }

    [Theory]
    [InlineData(
        "[CheatEnginePlugin(\"Demo\")] public sealed class {|#0:CESDK|} : CheatEnginePlugin { protected override void OnEnable() { } protected override void OnDisable() { } }",
        "CESDK")]
    [InlineData(
        "public class CESDK { [CheatEnginePlugin(\"Demo\")] public sealed class {|#0:Inner|} : CheatEnginePlugin { protected override void OnEnable() { } protected override void OnDisable() { } } }",
        "CESDK.Inner")]
    public async Task Class_that_takes_the_name_of_the_generated_entry_point_reports_reserved_name(string declaration,
        string displayName)
    {
        var directCollision = string.Equals(displayName, "CESDK", StringComparison.Ordinal);
        var collision = directCollision
            ? Verifier.Diagnostic(DiagnosticDescriptors.GeneratedEntryPointCollision)
                .WithSpan("Test0.cs", 6, 55, 6, 60)
                .WithArguments("CESDK")
            : Verifier.Diagnostic(DiagnosticDescriptors.GeneratedEntryPointCollision)
                .WithSpan("Test0.cs", 6, 20, 6, 25)
                .WithArguments("CESDK");
        var reservedName = Problem(0, displayName, PluginShapeIssues.ReservedEntryPointName);

        await Verifier.VerifyAsync(
            $$"""
                using CheatEngine.SDK.Annotations.Plugin;
                using CheatEngine.SDK.Hosting.Plugin;

                namespace {|#1:CESDK|}
                {
                    {{declaration}}
                }
              """,
            directCollision
                ?
                [
                    Verifier.Diagnostic(DiagnosticDescriptors.ReservedNamespace).WithLocation(1).WithArguments("CESDK"),
                    reservedName,
                    collision
                ]
                :
                [
                    Verifier.Diagnostic(DiagnosticDescriptors.ReservedNamespace).WithLocation(1).WithArguments("CESDK"),
                    collision,
                    reservedName
                ]);
    }

    [Fact]
    public async Task Class_named_like_the_entry_point_in_another_namespace_reports_nothing()
    {
        await Verifier.VerifyAsync("""
                                   using CheatEngine.SDK.Annotations.Plugin;
                                   using CheatEngine.SDK.Hosting.Plugin;

                                   namespace MyPlugin;

                                   [CheatEnginePlugin("Demo")]
                                   public sealed class CESDK : CheatEnginePlugin
                                   {
                                       protected override void OnEnable() { }
                                       protected override void OnDisable() { }
                                   }
                                   """);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null!")]
    public async Task Unusable_display_name_reports_on_the_attribute(string name)
    {
        await Verifier.VerifyAsync(
            $$"""
              using CheatEngine.SDK.Annotations.Plugin;
              using CheatEngine.SDK.Hosting.Plugin;

              namespace MyPlugin;

              [{|#0:CheatEnginePlugin({{name}})|}]
              public sealed class DemoPlugin : CheatEnginePlugin
              {
                  protected override void OnEnable() { }
                  protected override void OnDisable() { }
              }
              """,
            Problem(0, "DemoPlugin", PluginShapeIssues.InvalidName));
    }

    [Fact]
    public async Task Several_problems_report_one_diagnostic_each()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Plugin;

            namespace MyPlugin;

            public class Container
            {
                [CheatEnginePlugin("Demo")]
                private abstract class {|#0:DemoPlugin|}<T>
                {
                    private DemoPlugin() { }
                }
            }
            """,
            Problem(0, "Container.DemoPlugin<T>", PluginShapeIssues.Abstract),
            Problem(0, "Container.DemoPlugin<T>", PluginShapeIssues.Generic),
            Problem(0, "Container.DemoPlugin<T>", PluginShapeIssues.NotDerivedFromPluginBase),
            Problem(0, "Container.DemoPlugin<T>", PluginShapeIssues.Inaccessible),
            Problem(0, "Container.DemoPlugin<T>", PluginShapeIssues.InaccessibleParameterlessConstructor));
    }

    [Fact]
    public async Task Attribute_through_an_alias_is_recognised()
    {
        await Verifier.VerifyAsync(
            """
            using CheatEngine.SDK.Hosting.Plugin;
            using Plugin = CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute;

            namespace MyPlugin;

            [Plugin("Demo")]
            public abstract class {|#0:DemoPlugin|} : CheatEnginePlugin
            {
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.Abstract));
    }

    [Fact]
    public async Task Attribute_with_a_fully_qualified_name_is_recognised()
    {
        await Verifier.VerifyAsync(
            """
            namespace MyPlugin;

            [global::CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute("Demo")]
            public abstract class {|#0:DemoPlugin|} : global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
            {
            }
            """,
            Problem(0, "DemoPlugin", PluginShapeIssues.Abstract));
    }

    [Fact]
    public async Task Look_alike_attribute_from_another_namespace_is_ignored()
    {
        await Verifier.VerifyAsync("""
                                   using System;

                                   namespace MyPlugin;

                                   [AttributeUsage(AttributeTargets.Class)]
                                   public sealed class CheatEnginePluginAttribute(string name) : Attribute
                                   {
                                       public string Name { get; } = name;
                                   }

                                   [CheatEnginePlugin("Demo")]
                                   public abstract class DemoPlugin
                                   {
                                   }
                                   """);
    }

    [Fact]
    public async Task Partial_class_reports_once_on_the_part_that_carries_the_attribute()
    {
        await Verifier.VerifyAsync(
            [
                ("Plugin.cs", """
                              using CheatEngine.SDK.Annotations.Plugin;

                              namespace MyPlugin;

                              [CheatEnginePlugin("Demo")]
                              public partial class {|#0:DemoPlugin|}
                              {
                              }
                              """),
                ("Plugin.Lifecycle.cs", """
                                        using CheatEngine.SDK.Hosting.Plugin;

                                        namespace MyPlugin;

                                        public abstract partial class DemoPlugin : CheatEnginePlugin
                                        {
                                            private DemoPlugin() { }
                                        }
                                        """)
            ],
            Problem(0, "DemoPlugin", PluginShapeIssues.Abstract),
            Problem(0, "DemoPlugin", PluginShapeIssues.InaccessibleParameterlessConstructor));
    }

    [Fact]
    public async Task Class_in_generated_code_is_not_analysed()
    {
        await Verifier.VerifyAsync("""
                                   // <auto-generated/>
                                   using CheatEngine.SDK.Annotations.Plugin;

                                   namespace MyPlugin;

                                   [CheatEnginePlugin("Demo")]
                                   public abstract class DemoPlugin
                                   {
                                   }
                                   """);
    }

    [Fact]
    public async Task Attribute_on_a_struct_is_left_to_the_compiler()
    {
        await Verifier.VerifyAsync("""
                                   using CheatEngine.SDK.Annotations.Plugin;

                                   namespace MyPlugin;

                                   [{|CS0592:CheatEnginePlugin|}("Demo")]
                                   public struct DemoPlugin
                                   {
                                   }
                                   """);
    }

    [Fact]
    public async Task Project_without_a_cheatengine_sdk_reference_is_not_analysed()
    {
        await Verifier.VerifyWithoutCheatEngineSdkAsync("""
                                                        using System;

                                                        namespace CESDK.Lookalike
                                                        {
                                                            [AttributeUsage(AttributeTargets.Class)]
                                                            public sealed class CheatEnginePluginAttribute(string name) : Attribute
                                                            {
                                                                public string Name { get; } = name;
                                                            }

                                                            [CheatEnginePlugin("Demo")]
                                                            public abstract class DemoPlugin
                                                            {
                                                            }
                                                        }
                                                        """);
    }

    private static DiagnosticResult Problem(int location, string className, PluginShapeIssues problem)
    {
        return Verifier.Diagnostic(DiagnosticDescriptors.InvalidPluginClass)
            .WithLocation(location)
            .WithArguments(className, PluginClassProblemText.Describe(problem));
    }
}
