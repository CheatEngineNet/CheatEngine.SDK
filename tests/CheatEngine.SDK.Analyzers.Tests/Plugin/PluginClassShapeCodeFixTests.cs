using CheatEngine.SDK.Analyzers.CodeFixes.Plugin;
using Verifier = CheatEngine.SDK.Analyzers.Tests.Infrastructure.CodeFixVerifier<
    CheatEngine.SDK.Analyzers.Plugin.CheatEnginePluginAnalyzer,
    CheatEngine.SDK.Analyzers.CodeFixes.Plugin.PluginClassShapeCodeFixProvider>;

namespace CheatEngine.SDK.Analyzers.Tests.Plugin;

/// <summary>
///     The CESDK0001 fixes: one action per mechanical problem, none for the design problems. Tests with two plugin
///     classes exercise Fix All; CESDK0002 is then expected before and after, because no fix removes a plugin class.
/// </summary>
public sealed class PluginClassShapeCodeFixTests
{
    [Fact]
    public async Task Abstract_class_becomes_sealed()
    {
        await Verifier.VerifyAsync(
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public abstract partial class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                   {
                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public sealed partial class DemoPlugin : CheatEnginePlugin
                   {
                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            PluginClassShapeCodeFixProvider.MakeSealedEquivalenceKey);
    }

    [Fact]
    public async Task Static_class_becomes_sealed_and_the_next_problem_shows_up()
    {
        await Verifier.VerifyAsync(
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   internal static class {|CESDK0001:DemoPlugin|}
                   {
                       public static int Counter;
                   }
                   """),
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   internal sealed class {|CESDK0001:DemoPlugin|}
                   {
                       public static int Counter;
                   }
                   """),
            PluginClassShapeCodeFixProvider.MakeSealedEquivalenceKey);
    }

    [Fact]
    public async Task Abstract_modifier_on_another_part_is_replaced_in_its_own_document()
    {
        await Verifier.VerifyAsync(
            [
                ("Plugin.cs", Plugin("""
                                     [CheatEnginePlugin("Demo")]
                                     public partial class {|CESDK0001:DemoPlugin|}
                                     {
                                     }
                                     """)),
                ("Plugin.Lifecycle.cs", Plugin("""
                                               public abstract partial class DemoPlugin : CheatEnginePlugin
                                               {
                                                   protected override void OnEnable() { }
                                                   protected override void OnDisable() { }
                                               }
                                               """))
            ],
            [
                ("Plugin.cs", Plugin("""
                                     [CheatEnginePlugin("Demo")]
                                     public partial class DemoPlugin
                                     {
                                     }
                                     """)),
                ("Plugin.Lifecycle.cs", Plugin("""
                                               public sealed partial class DemoPlugin : CheatEnginePlugin
                                               {
                                                   protected override void OnEnable() { }
                                                   protected override void OnDisable() { }
                                               }
                                               """))
            ],
            PluginClassShapeCodeFixProvider.MakeSealedEquivalenceKey);
    }

    [Fact]
    public async Task Missing_constructor_is_added_in_front_of_the_first_constructor()
    {
        await Verifier.VerifyAsync(
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public sealed class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                   {
                       private readonly int _value;

                       public DemoPlugin(int value) => _value = value;

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public sealed class DemoPlugin : CheatEnginePlugin
                   {
                       private readonly int _value;

                       public DemoPlugin()
                       {
                       }

                       public DemoPlugin(int value) => _value = value;

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            PluginClassShapeCodeFixProvider.AddConstructorEquivalenceKey);
    }

    [Fact]
    public async Task Missing_constructor_is_added_after_the_fields_when_the_constructors_live_in_another_part()
    {
        await Verifier.VerifyAsync(
            [
                ("Plugin.cs", Plugin("""
                                     [CheatEnginePlugin("Demo")]
                                     public sealed partial class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                                     {
                                         private int _first;
                                         private int _second;

                                         protected override void OnEnable() => _first = _second;
                                         protected override void OnDisable() => _second = _first;
                                     }
                                     """)),
                ("Plugin.Construction.cs", Plugin("""
                                                  public sealed partial class DemoPlugin
                                                  {
                                                      public DemoPlugin(int value) => _first = value;
                                                  }
                                                  """))
            ],
            [
                ("Plugin.cs", Plugin("""
                                     [CheatEnginePlugin("Demo")]
                                     public sealed partial class DemoPlugin : CheatEnginePlugin
                                     {
                                         private int _first;
                                         private int _second;

                                         public DemoPlugin()
                                         {
                                         }

                                         protected override void OnEnable() => _first = _second;
                                         protected override void OnDisable() => _second = _first;
                                     }
                                     """)),
                ("Plugin.Construction.cs", Plugin("""
                                                  public sealed partial class DemoPlugin
                                                  {
                                                      public DemoPlugin(int value) => _first = value;
                                                  }
                                                  """))
            ],
            PluginClassShapeCodeFixProvider.AddConstructorEquivalenceKey);
    }

    [Theory]
    [InlineData("private DemoPlugin()")]
    [InlineData("protected DemoPlugin()")]
    [InlineData("private protected DemoPlugin()")]
    [InlineData("DemoPlugin()")]
    public async Task Hidden_constructor_becomes_public(string constructor)
    {
        await Verifier.VerifyAsync(
            Plugin($$"""
                     [CheatEnginePlugin("Demo")]
                     public class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                     {
                         // The comment stays in front of the constructor.
                         {{constructor}}
                         {
                         }

                         protected override void OnEnable() { }
                         protected override void OnDisable() { }
                     }
                     """),
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public class DemoPlugin : CheatEnginePlugin
                   {
                       // The comment stays in front of the constructor.
                       public DemoPlugin()
                       {
                       }

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            PluginClassShapeCodeFixProvider.MakeConstructorPublicEquivalenceKey);
    }

    [Fact]
    public async Task Hidden_constructor_in_another_part_becomes_public_in_its_own_document()
    {
        await Verifier.VerifyAsync(
            [
                ("Plugin.cs", Plugin("""
                                     [CheatEnginePlugin("Demo")]
                                     public sealed partial class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                                     {
                                         protected override void OnEnable() { }
                                         protected override void OnDisable() { }
                                     }
                                     """)),
                ("Plugin.Construction.cs", Plugin("""
                                                  public sealed partial class DemoPlugin
                                                  {
                                                      [System.Obsolete("Only to show that attributes stay in front of the modifier.")]
                                                      private unsafe DemoPlugin() { }
                                                  }
                                                  """))
            ],
            [
                ("Plugin.cs", Plugin("""
                                     [CheatEnginePlugin("Demo")]
                                     public sealed partial class DemoPlugin : CheatEnginePlugin
                                     {
                                         protected override void OnEnable() { }
                                         protected override void OnDisable() { }
                                     }
                                     """)),
                ("Plugin.Construction.cs", Plugin("""
                                                  public sealed partial class DemoPlugin
                                                  {
                                                      [System.Obsolete("Only to show that attributes stay in front of the modifier.")]
                                                      public unsafe DemoPlugin() { }
                                                  }
                                                  """))
            ],
            PluginClassShapeCodeFixProvider.MakeConstructorPublicEquivalenceKey);
    }

    [Fact]
    public async Task Comment_on_the_dropped_second_accessibility_keyword_is_kept()
    {
        await Verifier.VerifyAsync(
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                   {
                       private /* a */ protected /* b: documented reason */ DemoPlugin() { }

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public class DemoPlugin : CheatEnginePlugin
                   {
                       public /* a */ /* b: documented reason */ DemoPlugin() { }

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            PluginClassShapeCodeFixProvider.MakeConstructorPublicEquivalenceKey);
    }

    [Fact]
    public async Task Comment_on_a_dropped_keyword_that_follows_another_modifier_stays_in_place()
    {
        await Verifier.VerifyAsync(
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                   {
                       private unsafe protected // only derived plugins of this assembly
                           DemoPlugin() { }

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            Plugin("""
                   [CheatEnginePlugin("Demo")]
                   public class DemoPlugin : CheatEnginePlugin
                   {
                       public unsafe // only derived plugins of this assembly
                           DemoPlugin() { }

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            PluginClassShapeCodeFixProvider.MakeConstructorPublicEquivalenceKey);
    }

    [Theory]
    [InlineData("protected PluginBase(int value) { }")]
    [InlineData("private PluginBase() { } protected PluginBase(int value) { }")]
    public async Task Missing_constructor_gets_no_fix_when_the_base_class_cannot_be_constructed_without_arguments(
        string baseConstructors)
    {
        var source = Plugin($$"""
                              public abstract class PluginBase : CheatEnginePlugin
                              {
                                  {{baseConstructors}}
                              }

                              [CheatEnginePlugin("Demo")]
                              public sealed class {|CESDK0001:DemoPlugin|} : PluginBase
                              {
                                  public DemoPlugin(int value) : base(value) { }

                                  protected override void OnEnable() { }
                                  protected override void OnDisable() { }
                              }
                              """);

        await Verifier.VerifyAsync(source, source);
    }

    [Fact]
    public async Task Missing_constructor_is_added_when_the_base_constructor_has_only_optional_parameters()
    {
        await Verifier.VerifyAsync(
            Plugin("""
                   public abstract class PluginBase : CheatEnginePlugin
                   {
                       protected PluginBase(int value = 0) { }
                   }

                   [CheatEnginePlugin("Demo")]
                   public sealed class {|CESDK0001:DemoPlugin|} : PluginBase
                   {
                       public DemoPlugin(int value) : base(value) { }

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            Plugin("""
                   public abstract class PluginBase : CheatEnginePlugin
                   {
                       protected PluginBase(int value = 0) { }
                   }

                   [CheatEnginePlugin("Demo")]
                   public sealed class DemoPlugin : PluginBase
                   {
                       public DemoPlugin()
                       {
                       }

                       public DemoPlugin(int value) : base(value) { }

                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            PluginClassShapeCodeFixProvider.AddConstructorEquivalenceKey);
    }

    [Fact]
    public async Task Required_members_and_obsolete_errors_get_no_fix()
    {
        var source = Plugin("""
                            [CheatEnginePlugin("Demo")]
                            public sealed class {|CESDK0001:{|CESDK0001:DemoPlugin|}|} : CheatEnginePlugin
                            {
                                [System.Obsolete("Construct through the host.", error: true)]
                                public DemoPlugin() { }

                                public required int Value { get; set; }

                                protected override void OnEnable() { }
                                protected override void OnDisable() { }
                            }
                            """);

        await Verifier.VerifyAsync(source, source);
    }

    [Fact]
    public async Task Primary_constructor_gets_no_fix()
    {
        var source = Plugin("""
                            [CheatEnginePlugin("Demo")]
                            public sealed class {|CESDK0001:DemoPlugin|}(int value) : CheatEnginePlugin
                            {
                                protected override void OnEnable() { }
                                protected override void OnDisable() => value.ToString();
                            }
                            """);

        await Verifier.VerifyAsync(source, source);
    }

    [Theory]
    [InlineData("public sealed class {|CESDK0001:DemoPlugin|}<T> : CheatEnginePlugin")]
    [InlineData("file sealed class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin")]
    [InlineData("public sealed class {|CESDK0001:DemoPlugin|} : Unrelated")]
    public async Task Design_problems_of_the_class_get_no_fix(string declaration)
    {
        var source = Plugin($$"""
                              public abstract class Unrelated
                              {
                                  protected abstract void OnEnable();
                                  protected abstract void OnDisable();
                              }

                              [CheatEnginePlugin("Demo")]
                              {{declaration}}
                              {
                                  protected override void OnEnable() { }
                                  protected override void OnDisable() { }
                              }
                              """);

        await Verifier.VerifyAsync(source, source);
    }

    [Fact]
    public async Task Hidden_nested_class_and_unusable_name_get_no_fix()
    {
        var source = Plugin("""
                            public class Container
                            {
                                [{|CESDK0001:CheatEnginePlugin("")|}]
                                private sealed class {|CESDK0001:DemoPlugin|} : CheatEnginePlugin
                                {
                                    protected override void OnEnable() { }
                                    protected override void OnDisable() { }
                                }
                            }
                            """);

        await Verifier.VerifyAsync(source, source);
    }

    [Fact]
    public async Task Fix_all_adds_the_constructor_to_every_plugin_class()
    {
        await Verifier.VerifyAsync(
            [
                ("First.cs", PluginWithoutConstructor("First", false)),
                ("Second.cs", PluginWithoutConstructor("Second", false))
            ],
            [
                ("First.cs", PluginWithoutConstructor("First", true)),
                ("Second.cs", PluginWithoutConstructor("Second", true))
            ],
            PluginClassShapeCodeFixProvider.AddConstructorEquivalenceKey);
    }

    [Fact]
    public async Task Fix_all_replaces_abstract_in_every_plugin_class_of_a_document()
    {
        await Verifier.VerifyAsync(
            Plugin("""
                   [CheatEnginePlugin("First")]
                   public abstract class {|CESDK0001:{|CESDK0002:FirstPlugin|}|} : CheatEnginePlugin
                   {
                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }

                   [CheatEnginePlugin("Second")]
                   public abstract class {|CESDK0001:{|CESDK0002:SecondPlugin|}|} : CheatEnginePlugin
                   {
                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            Plugin("""
                   [CheatEnginePlugin("First")]
                   public sealed class {|CESDK0002:FirstPlugin|} : CheatEnginePlugin
                   {
                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }

                   [CheatEnginePlugin("Second")]
                   public sealed class {|CESDK0002:SecondPlugin|} : CheatEnginePlugin
                   {
                       protected override void OnEnable() { }
                       protected override void OnDisable() { }
                   }
                   """),
            PluginClassShapeCodeFixProvider.MakeSealedEquivalenceKey);
    }

    private static string PluginWithoutConstructor(string name, bool fixedState)
    {
        var className = fixedState
            ? $$"""{|CESDK0002:{{name}}Plugin|}"""
            : $$"""{|CESDK0001:{|CESDK0002:{{name}}Plugin|}|}""";
        var constructor = fixedState ? $"    public {name}Plugin()\n    {{\n    }}\n\n" : string.Empty;
        return Plugin($$"""
                        [CheatEnginePlugin("{{name}}")]
                        public sealed class {{className}} : CheatEnginePlugin
                        {
                        {{constructor}}    public {{name}}Plugin(string text) { }

                            protected override void OnEnable() { }
                            protected override void OnDisable() { }
                        }
                        """);
    }

    // File-scoped namespace and the two usings around the type declarations under test.
    private static string Plugin(string types)
    {
        return $$"""
                 using CheatEngine.SDK.Annotations.Plugin;
                 using CheatEngine.SDK.Hosting.Plugin;

                 namespace MyPlugin;

                 {{types}}
                 """;
    }
}
