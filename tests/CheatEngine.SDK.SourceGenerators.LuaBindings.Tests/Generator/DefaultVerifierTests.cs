using System.Text;

using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     The same generator under the standard Roslyn test harness (
///     <c>Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing</c>
///     with the framework-agnostic <see cref="DefaultVerifier" />): the generator is instantiated by type, like the
///     compiler does, and the library compares the generated sources and fails on any compiler warning.
/// </summary>
public sealed class DefaultVerifierTests
{
	[Fact]
	public async Task Verifier_two_functions_match_expected_source_and_compile()
	{
		CSharpSourceGeneratorTest<LuaBindingsGenerator, DefaultVerifier> test = CreateTest(BindingSources.Functions);
		test.TestState.GeneratedSources.Add((
			typeof(LuaBindingsGenerator),
			ExpectedFiles.FunctionsHintName,
			SourceText.From(ExpectedFiles.Functions(), Encoding.UTF8)));

		Assert.Single(test.TestState.GeneratedSources);
		await test.RunAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Verifier_two_globals_match_expected_source_and_compile()
	{
		CSharpSourceGeneratorTest<LuaBindingsGenerator, DefaultVerifier> test = CreateTest(BindingSources.Globals);
		test.TestState.GeneratedSources.Add((
			typeof(LuaBindingsGenerator),
			ExpectedFiles.GlobalsHintName,
			SourceText.From(ExpectedFiles.Globals(), Encoding.UTF8)));

		Assert.Single(test.TestState.GeneratedSources);
		await test.RunAsync(TestContext.Current.CancellationToken);
	}

	private static CSharpSourceGeneratorTest<LuaBindingsGenerator, DefaultVerifier> CreateTest(string source)
	{
		RoslynEnvironment environment = RoslynEnvironment.Shared;

		CSharpSourceGeneratorTest<LuaBindingsGenerator, DefaultVerifier> test = new()
		{
			// A framework moniker WITHOUT a reference-assembly package: nothing is resolved through NuGet; the
			// framework and the SDK come from the local installation and this process (no network).
			ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
			CompilerDiagnostics = CompilerDiagnostics.Warnings
		};

		// The test declarations are undocumented public members: CS1591 is theirs, not the generated file's.
		test.DisabledDiagnostics.Add("CS1591");
		test.TestState.Sources.Add(source);
		test.TestState.AdditionalReferences.AddRange(environment.FrameworkReferences);
		test.TestState.AdditionalReferences.AddRange(environment.SdkReferences);
		return test;
	}
}
