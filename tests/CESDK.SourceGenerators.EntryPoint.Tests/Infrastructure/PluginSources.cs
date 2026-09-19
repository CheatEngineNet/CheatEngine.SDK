namespace CESDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>Plugin source snippets shared by the tests. They compile warning-free against the contract stubs.</summary>
internal static class PluginSources
{
    /// <summary>Overrides of the two abstract lifecycle methods, to paste into a plugin class body.</summary>
    public const string LifecycleOverrides = """
                                             protected override void OnEnable() { }

                                                 protected override void OnDisable() { }
                                             """;

    /// <summary>The nominal plugin: <c>Demo.DemoPlugin</c>, display name <c>Demo Plugin</c>.</summary>
    public const string Nominal = $$"""
                                    using CESDK.Annotations.Plugin;
                                    using CESDK.Hosting.Plugin;

                                    namespace Demo;

                                    [CheatEnginePlugin("Demo Plugin")]
                                    public sealed class DemoPlugin : CheatEnginePlugin
                                    {
                                        {{LifecycleOverrides}}
                                    }
                                    """;

    /// <summary>A valid plugin <c>Demo.&lt;className&gt;</c> whose name argument is <paramref name="nameExpression" />.</summary>
    /// <param name="nameExpression">C# expression placed between the attribute's parentheses.</param>
    /// <param name="className">Simple name of the plugin class.</param>
    public static string WithNameExpression(string nameExpression, string className = "DemoPlugin")
    {
        return $$"""
                 using CESDK.Annotations.Plugin;
                 using CESDK.Hosting.Plugin;

                 namespace Demo;

                 [CheatEnginePlugin({{nameExpression}})]
                 public sealed class {{className}} : CheatEnginePlugin
                 {
                     {{LifecycleOverrides}}
                 }
                 """;
    }
}
