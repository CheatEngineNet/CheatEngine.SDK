using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Plugin;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;

using Verifier = CheatEngine.SDK.Analyzers.Tests.Infrastructure.AnalyzerVerifier<
	CheatEngine.SDK.Analyzers.Plugin.CheatEnginePluginAnalyzer>;

namespace CheatEngine.SDK.Analyzers.Tests.Plugin;

/// <summary>
///     The MSBuild switch <c>CheatEngineSdkGenerateEntryPoint</c>: CESDK0001 and CESDK0002 state what the generated entry
///     point
///     needs and fall silent with it; CESDK0004 is about Cheat Engine's own lookup and stays.
/// </summary>
public sealed class EntryPointSwitchTests
{
	private const string SwitchName = "CheatEngineSdkGenerateEntryPoint";

	// Two plugin classes, one of them abstract, in a namespace under CESDK: all three rules have something to say.
	private const string Source = """
	                              using CheatEngine.SDK.Annotations.Plugin;
	                              using CheatEngine.SDK.Hosting.Plugin;

	                              namespace {|#0:CESDK.MyPlugin|}
	                              {

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
	                              }
	                              """;

	private const string SourceWithManualBootstrap = Source + """

	                                                          namespace CESDK
	                                                          {
	                                                              public static class CESDK
	                                                              {
	                                                                  public static int CEPluginInitialize(System.IntPtr _, int __) => 1;
	                                                              }
	                                                          }
	                                                          """;

	[Theory]
	[InlineData("false")]
	[InlineData("False")]
	[InlineData(" FALSE ")]
	public async Task Switched_off_entry_point_accepts_a_valid_manual_bootstrap(string value)
	{
		await Verifier.VerifyWithBuildPropertyAsync(
			SwitchName,
			value,
			SourceWithManualBootstrap);
	}

	[Fact]
	public async Task Switched_off_entry_point_rejects_generic_or_by_ref_bootstrap_lookalikes()
	{
		const string source = Source + """

		                               namespace CESDK
		                               {
		                                   public static class {|#3:CESDK|}
		                                   {
		                                       public static int CEPluginInitialize<T>(System.IntPtr _, int __) => 1;
		                                       public static int CEPluginInitialize(ref System.IntPtr _, int __) => 1;
		                                   }
		                               }
		                               """;

		await Verifier.VerifyWithBuildPropertyAsync(
			SwitchName,
			"false",
			source,
			Verifier.Diagnostic(DiagnosticDescriptors.InvalidManualBootstrap).WithLocation(3)
				.WithArguments("CESDK.CESDK has no public static int CEPluginInitialize(System.IntPtr, int) method"));
	}

	[Theory]
	[InlineData("true")]
	[InlineData("True")]
	public async Task Explicit_true_keeps_every_generated_entry_point_rule_on(string value)
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

	[Theory]
	[InlineData("")]
	[InlineData("maybe")]
	public async Task Missing_or_invalid_switch_keeps_generation_rules_silent(string value)
	{
		await Verifier.VerifyWithBuildPropertyAsync(SwitchName, value, Source);
	}
}
