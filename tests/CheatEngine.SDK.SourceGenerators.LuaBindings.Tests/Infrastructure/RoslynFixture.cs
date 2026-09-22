using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>
///     Class fixture of every test class that runs the generator: a direct <see cref="CSharpGeneratorDriver" /> harness
///     over the shared <see cref="RoslynEnvironment" />. It always tracks incremental steps, so that any run can be
///     inspected for caching.
/// </summary>
public sealed class RoslynFixture
{
	/// <summary>Assembly name of the plugin compilations created here.</summary>
	internal const string PluginAssemblyName = "TestBindings";

	/// <summary>
	///     Takes the process-wide environment; a failure to find the framework references fails the class with the
	///     resolver's message.
	/// </summary>
	public RoslynFixture()
	{
		Environment = RoslynEnvironment.Shared;
	}

	internal RoslynEnvironment Environment
	{
		get;
	}

	/// <summary>
	///     A plugin compilation (unsafe allowed) with one syntax tree per source, named <c>Source0.cs</c>,
	///     <c>Source1.cs</c>...
	/// </summary>
	internal CSharpCompilation CreateCompilation(params string[] sources)
	{
		return CreateCompilation(RoslynEnvironment.CompilationOptions, sources);
	}

	/// <summary>Same, with other compilation options (for example unsafe off).</summary>
	internal CSharpCompilation CreateCompilation(CSharpCompilationOptions options, params string[] sources)
	{
		SyntaxTree[] trees = new SyntaxTree[sources.Length];
		for (int i = 0; i < sources.Length; i++)
		{
			string path = string.Create(CultureInfo.InvariantCulture, $"Source{i}.cs");
			trees[i] = Parse(sources[i], path);
		}

		return CSharpCompilation.Create(PluginAssemblyName, trees, Environment.PluginReferences, options);
	}

	/// <summary>Creates a driver for the generator with step tracking on.</summary>
	internal static GeneratorDriver CreateDriver()
	{
		return CSharpGeneratorDriver.Create(
			[new LuaBindingsGenerator().AsSourceGenerator()],
			[],
			RoslynEnvironment.ParseOptions,
			null,
			new GeneratorDriverOptions(
				IncrementalGeneratorOutputKind.None,
				true));
	}

	/// <summary>Runs the generator once over <paramref name="sources" />.</summary>
	internal GeneratorRun Run(params string[] sources)
	{
		return Run(CreateCompilation(sources));
	}

	/// <summary>Runs the generator once over <paramref name="compilation" />.</summary>
	internal static GeneratorRun Run(Compilation compilation)
	{
		return GeneratorRun.Execute(CreateDriver(), compilation);
	}

	internal static SyntaxTree Parse(string source, string path)
	{
		return CSharpSyntaxTree.ParseText(source, RoslynEnvironment.ParseOptions, path,
			cancellationToken: TestContext.Current.CancellationToken);
	}
}
