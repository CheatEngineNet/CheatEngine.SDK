using CESDK.Analyzers.Diagnostics;
using CESDK.Analyzers.Plugin;
using CESDK.SourceGenerators.Shared.Shapes;
using Verifier =
    CESDK.Analyzers.Tests.Infrastructure.AnalyzerVerifier<CESDK.Analyzers.Plugin.CheatEnginePluginAnalyzer>;

namespace CESDK.Analyzers.Tests.Plugin;

/// <summary>
///     The MSBuild switch <c>CesdkGenerateEntryPoint</c>: CESDK0001 and CESDK0002 state what the generated entry point
///     needs and fall silent with it; CESDK0004 is about Cheat Engine's own lookup and stays.
/// </summary>
public sealed class EntryPointSwitchTests
{
    private const string SwitchName = "CesdkGenerateEntryPoint";

    // Two plugin classes, one of them abstract, in a namespace under CESDK: all three rules have something to say.
    private const string Source = """
                                  using CESDK.Annotations.Plugin;
                                  using CESDK.Hosting.Plugin;

                                  namespace {|#0:CESDK.MyPlugin|};

                                  [CheatEnginePlugin("First")]
                                  public abstract class {|#1:FirstPlugin|} : CheatEnginePlugin
                                  {
                                  }

                                  [CheatEnginePlugin("Second")]
                                  public sealed class {|#2:SecondPlugin|} : FirstPlugin
                                  {
                                      protected override void OnEnable() { }
                                      protected override void OnDisable() { }
                                  }
                                  """;

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData(" FALSE ")]
    public async Task Switched_off_entry_point_silences_shape_and_count_but_not_the_namespace(string value)
    {
        await Verifier.VerifyWithBuildPropertyAsync(
            SwitchName,
            value,
            Source,
            Verifier.Diagnostic(DiagnosticDescriptors.ReservedNamespace).WithLocation(0)
                .WithArguments("CESDK.MyPlugin"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("")]
    [InlineData("maybe")]
    public async Task Any_other_value_keeps_every_rule_on(string value)
    {
        await Verifier.VerifyWithBuildPropertyAsync(
            SwitchName,
            value,
            Source,
            Verifier.Diagnostic(DiagnosticDescriptors.ReservedNamespace).WithLocation(0)
                .WithArguments("CESDK.MyPlugin"),
            Verifier.Diagnostic(DiagnosticDescriptors.InvalidPluginClass)
                .WithLocation(1)
                .WithArguments("FirstPlugin", PluginClassProblemText.Describe(PluginShapeIssues.Abstract)),
            Verifier.Diagnostic(DiagnosticDescriptors.MultiplePluginClasses).WithLocation(1)
                .WithArguments("FirstPlugin", 2),
            Verifier.Diagnostic(DiagnosticDescriptors.MultiplePluginClasses).WithLocation(2)
                .WithArguments("SecondPlugin", 2));
    }
}
