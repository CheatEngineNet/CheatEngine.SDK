using System.Text;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using CheatEngine.SDK.SourceGenerators.Shared;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

public sealed class NominalOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	[Fact]
	public void Generator_single_valid_plugin_emits_exact_bootstrap()
	{
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);

		GeneratedSourceResult generated = Assert.Single(run.GeneratedSources);
		Assert.Equal(ExpectedBootstrap.HintName, generated.HintName);
		Assert.Equal(
			ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"),
			generated.SourceText.ToString());
	}

	[Fact]
	public void Generator_single_valid_plugin_output_is_utf8_with_lf_line_endings()
	{
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);

		GeneratedSourceResult generated = Assert.Single(run.GeneratedSources);
		Assert.Equal(Encoding.UTF8, generated.SourceText.Encoding);
		Assert.DoesNotContain('\r', generated.SourceText.ToString());
	}

	[Fact]
	public void Generator_single_valid_plugin_output_compiles_without_errors_or_warnings()
	{
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_single_valid_plugin_output_contains_no_unsafe_code()
	{
		// The compilation options of the harness forbid unsafe code, so compiling clean already proves it; the
		// textual check documents the contract.
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);

		Assert.DoesNotContain("unsafe", run.SingleGeneratedText, StringComparison.Ordinal);
		Assert.False(run.OutputCompilation.Options is CSharpCompilationOptions { AllowUnsafe: true });
	}

	[Fact]
	public void Generator_single_valid_plugin_declares_the_host_mandated_symbols()
	{
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);

		INamedTypeSymbol? entryPoint = run.OutputCompilation.Assembly.GetTypeByMetadataName("CESDK.CESDK");
		Assert.NotNull(entryPoint);
		Assert.True(entryPoint.IsStatic);
		Assert.Equal(Accessibility.Internal, entryPoint.DeclaredAccessibility);

		IMethodSymbol initialize =
			Assert.IsType<IMethodSymbol>(Assert.Single(entryPoint.GetMembers("CEPluginInitialize")), false);
		Assert.True(initialize.IsStatic);
		Assert.Equal(Accessibility.Public, initialize.DeclaredAccessibility);
		Assert.Equal(SpecialType.System_Int32, initialize.ReturnType.SpecialType);
		Assert.Equal(2, initialize.Parameters.Length);
		Assert.Equal(SpecialType.System_IntPtr, initialize.Parameters[0].Type.SpecialType);
		Assert.Equal(SpecialType.System_Int32, initialize.Parameters[1].Type.SpecialType);
	}

	[Fact]
	public void ManagedEntryPointNames_matches_CheatEngine_SDK_Abi_ManagedEntryPoint()
	{
		// Parity check for the generator-side identity constants BootstrapEmitter writes (source-generators/CheatEngine.SDK.SourceGenerators.Shared/
		// ManagedEntryPointNames.cs) against their net10.0-side source of truth: the netstandard2.0 Roslyn component
		// cannot reference CheatEngine.SDK.Abi directly, so this is what keeps the two copies from drifting.
		Assert.Equal(ManagedEntryPoint.Namespace, ManagedEntryPointNames.Namespace);
		Assert.Equal(ManagedEntryPoint.TypeName, ManagedEntryPointNames.TypeName);
		Assert.Equal(ManagedEntryPoint.MethodName, ManagedEntryPointNames.MethodName);
	}

	[Fact]
	public void Generator_any_input_reports_no_diagnostics()
	{
		GeneratorRun valid = roslyn.Run(PluginSources.Nominal);
		GeneratorRun invalid =
			roslyn.Run(PluginSources.Nominal.Replace("sealed", "abstract", StringComparison.Ordinal));

		Assert.Empty(valid.GeneratorDiagnostics);
		Assert.Empty(valid.Result.Diagnostics);
		Assert.Empty(invalid.GeneratorDiagnostics);
		Assert.Empty(invalid.Result.Diagnostics);
	}

	[Fact]
	public void Generator_bootstrap_constructs_the_plugin_without_reflection()
	{
		// Audit A04-13: the plugin type is known at compile time; eng/BannedSymbols.txt bans the reflection routes.
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);
		string text = run.SingleGeneratedText;

		foreach (string reflective in (string[]) ["Activator", "CreateInstance", "GetTypes", "GetType(", "typeof(",
					 "System.Reflection", "Assembly."])
		{
			Assert.DoesNotContain(reflective, text, StringComparison.Ordinal);
		}

		Assert.Contains("Create() => new global::Demo.DemoPlugin();", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_entry_point_catches_every_exception_before_native_code()
	{
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);
		MethodDeclarationSyntax initialize = CSharpSyntaxTree.ParseText(run.SingleGeneratedText,
				cancellationToken: TestContext.Current.CancellationToken)
			.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
			.Single(static method =>
				string.Equals(method.Identifier.ValueText, "CEPluginInitialize", StringComparison.Ordinal));

		// One try statement is the whole body; its only handler catches System.Exception unfiltered and returns 0.
		TryStatementSyntax body = Assert.IsType<TryStatementSyntax>(Assert.Single(initialize.Body!.Statements));
		CatchClauseSyntax handler = Assert.Single(body.Catches);
		Assert.Equal("global::System.Exception", handler.Declaration!.Type.ToString());
		Assert.Null(handler.Filter);
		Assert.Null(body.Finally);
		ReturnStatementSyntax fallback = Assert.IsType<ReturnStatementSyntax>(Assert.Single(handler.Block.Statements));
		Assert.Equal("0", fallback.Expression!.ToString());
	}
}
