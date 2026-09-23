using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Plugin;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

using Verifier = CheatEngine.SDK.Analyzers.Tests.Infrastructure.AnalyzerVerifier<
	CheatEngine.SDK.Analyzers.Plugin.ClassicNativeExportAnalyzer>;

namespace CheatEngine.SDK.Analyzers.Tests.Plugin;

/// <summary>
///     CESDK0006: an <c>[UnmanagedCallersOnly]</c> export named <c>CEPlugin_*</c>, the classic native plugin entry
///     points that only a NativeAOT publication of the consumer's own assembly can produce, is flagged; that NativeAOT
///     plugin DLL route is not a supported CheatEngine.SDK profile (Cheat Engine unloads with <c>FreeLibrary</c>, audit
///     F02). Other entry points and the historical unprefixed names stay silent.
/// </summary>
public sealed class ClassicNativeExportAnalyzerTests
{
	[Theory]
	[InlineData("CEPlugin_GetVersion")]
	[InlineData("CEPlugin_InitializePlugin")]
	[InlineData("CEPlugin_DisablePlugin")]
	[InlineData("CEPlugin_Custom")]
	[Trait("Qualification", "Q41")]
	public async Task UnmanagedCallersOnly_entry_point_with_the_CEPlugin_prefix_reports_CESDK0006(string entryPoint)
	{
		await Verifier.VerifyAsync(Exports($$"""
		                                     [{|#0:UnmanagedCallersOnly(EntryPoint = "{{entryPoint}}", CallConvs = new[] { typeof(CallConvStdcall) })|}]
		                                     private static int Export(nint argument, int size) => 0;
		                                     """),
			Verifier.Diagnostic(DiagnosticDescriptors.ClassicNativePluginExport).WithLocation(0)
				.WithArguments("Export", entryPoint));
	}

	[Fact]
	[Trait("Qualification", "Q41")]
	public async Task NativeExportNames_constant_as_entry_point_reports_CESDK0006()
	{
		CheatEngineSdkAnalyzerTest<ClassicNativeExportAnalyzer> test = new();
		test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(NativeExportNames).Assembly.Location));
		test.TestState.Sources.Add(("Test0.cs", TestText.Normalize(Exports("""
			[{|#0:UnmanagedCallersOnly(EntryPoint = NativeExportNames.GetVersion)|}]
			private static int GetVersion(nint version, int size) => 0;

			[{|#1:UnmanagedCallersOnly(EntryPoint = NativeExportNames.InitializePlugin)|}]
			private static int Initialize(nint exports, int pluginId) => 0;

			[{|#2:UnmanagedCallersOnly(EntryPoint = NativeExportNames.DisablePlugin)|}]
			private static int Disable() => 0;
			""", "using CheatEngine.SDK.Abi.Native;"))));
		test.ExpectedDiagnostics.Add(Verifier.Diagnostic(DiagnosticDescriptors.ClassicNativePluginExport).WithLocation(0)
			.WithArguments("GetVersion", NativeExportNames.GetVersion));
		test.ExpectedDiagnostics.Add(Verifier.Diagnostic(DiagnosticDescriptors.ClassicNativePluginExport).WithLocation(1)
			.WithArguments("Initialize", NativeExportNames.InitializePlugin));
		test.ExpectedDiagnostics.Add(Verifier.Diagnostic(DiagnosticDescriptors.ClassicNativePluginExport).WithLocation(2)
			.WithArguments("Disable", NativeExportNames.DisablePlugin));

		await test.RunAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Local_function_export_with_the_CEPlugin_prefix_reports_CESDK0006()
	{
		await Verifier.VerifyAsync(Exports("""
		                                   private static void Host()
		                                   {
		                                       [{|#0:UnmanagedCallersOnly(EntryPoint = "CEPlugin_Local")|}]
		                                       static int Local() => 0;
		                                   }
		                                   """),
			Verifier.Diagnostic(DiagnosticDescriptors.ClassicNativePluginExport).WithLocation(0)
				.WithArguments("Local", "CEPlugin_Local"));
	}

	[Theory]
	[InlineData("[UnmanagedCallersOnly]")]
	[InlineData("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]")]
	[InlineData("[UnmanagedCallersOnly(EntryPoint = \"probe_name_query\")]")]
	[InlineData("[UnmanagedCallersOnly(EntryPoint = \"ceplugin_GetVersion\")]")]
	[InlineData("[UnmanagedCallersOnly(EntryPoint = \"MyCEPlugin_GetVersion\")]")]
	[InlineData("[UnmanagedCallersOnly(EntryPoint = \"CEPlugin\")]")]
	public async Task Other_entry_points_and_UnmanagedCallersOnly_without_entry_point_report_nothing(string attribute)
	{
		await Verifier.VerifyAsync(Exports($$"""
		                                     {{attribute}}
		                                     private static int Export(nint argument) => 0;
		                                     """));
	}

	[Theory]
	[InlineData("GetVersion")]
	[InlineData("InitializePlugin")]
	[InlineData("DisablePlugin")]
	public async Task Unprefixed_historical_names_report_nothing(string entryPoint)
	{
		await Verifier.VerifyAsync(Exports($$"""
		                                     [UnmanagedCallersOnly(EntryPoint = "{{entryPoint}}")]
		                                     private static int Export(nint argument) => 0;
		                                     """));
	}

	[Fact]
	public async Task Project_without_a_cheatengine_sdk_reference_is_not_analysed()
	{
		await Verifier.VerifyWithoutCheatEngineSdkAsync(Exports("""
		                                                        [UnmanagedCallersOnly(EntryPoint = "CEPlugin_GetVersion")]
		                                                        private static int Export(nint argument) => 0;
		                                                        """));
	}

	[Fact]
	public async Task Export_in_generated_code_is_not_analysed()
	{
		await Verifier.VerifyAsync([("Exports.g.cs", "// <auto-generated/>\n" + Exports("""
			[UnmanagedCallersOnly(EntryPoint = "CEPlugin_GetVersion")]
			private static int Export(nint argument) => 0;
			"""))]);
	}

	private static string Exports(string members, string extraUsing = "")
	{
		return $$"""
		         using System.Runtime.CompilerServices;
		         using System.Runtime.InteropServices;
		         {{extraUsing}}

		         namespace Sample;

		         public static class NativePluginExports
		         {
		         {{members}}
		         }
		         """;
	}
}
