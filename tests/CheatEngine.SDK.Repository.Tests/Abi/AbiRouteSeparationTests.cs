using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;
using CheatEngine.SDK.Repository.Tests.SourceScanning;

namespace CheatEngine.SDK.Repository.Tests.Abi;

/// <summary>
///     The two exported-function tables stay separate routes (audit A17-02, AX04-11, AX05-06, A11-21): a managed plugin
///     receives the 48-byte <c>ManagedExportedFunctions</c>, a classic native plugin the 159-slot table whose first 144
///     bytes <c>ExportedFunctionsPrefix</c> maps, and no production code converts one into the other or reaches the
///     classic prefix outside the classic dispatcher and readers. Comments and literals are ignored.
/// </summary>
public sealed partial class AbiRouteSeparationTests
{
	private const string Managed = "ManagedExportedFunctions";
	private const string Prefix = "ExportedFunctionsPrefix";

	/// <summary>The declaration of the prefix and the only code allowed to consume it.</summary>
	private static readonly string[] PrefixConsumers =
	[
		"libs/CheatEngine.SDK.Abi/Native/ExportedFunctionsPrefix.cs",
		"libs/CheatEngine.SDK.Abi/Native/ClassicDebugEventDispatcher.cs",
		"libs/CheatEngine.SDK.Abi/Native/ClassicExportedFunctionsPrefixReader.cs",
		"libs/CheatEngine.SDK.Abi/Native/ClassicExportedFunctionsSlotReader.cs"
	];

	[Fact]
	public void Managed_exports_are_never_converted_to_the_classic_prefix()
	{
		List<string> problems = [];
		foreach ((string file, string code) in ProductionCode())
		{
			if (Identifier(Managed).IsMatch(code) && Identifier(Prefix).IsMatch(code))
			{
				problems.Add($"{file} names both {Managed} and {Prefix}: the managed and classic tables are separate routes.");
			}

			foreach (Match cast in PrefixPointerCast().Matches(code))
			{
				problems.Add($"{file}:{CSharpCode.LineOf(code, cast.Index)} casts to {Prefix}*; read the prefix with ClassicExportedFunctionsPrefixReader.");
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void Only_the_classic_dispatcher_and_readers_consume_the_prefix()
	{
		string[] consumers =
		[
			.. ProductionCode().Where(static source => Identifier(Prefix).IsMatch(source.Code))
				.Select(static source => source.File)
		];

		Assert.All(consumers, file => Assert.Contains(file, PrefixConsumers, StringComparer.Ordinal));
		Assert.Contains("libs/CheatEngine.SDK.Abi/Native/ClassicDebugEventDispatcher.cs", consumers, StringComparer.Ordinal);
		Assert.All(PrefixConsumers, file => Assert.True(RepositoryDocument.Exists(file), $"{file} no longer exists."));
		Assert.DoesNotContain(consumers, static file => file.StartsWith("libs/CheatEngine.SDK.Hosting/", StringComparison.Ordinal));
	}

	[Fact]
	public void The_rules_see_code_and_ignore_comments_and_strings()
	{
		const string Documented = """
								  /// <summary>Unlike ManagedExportedFunctions, the ExportedFunctionsPrefix is classic.</summary>
								  class C { string s = "ExportedFunctionsPrefix*"; }
								  """;
		const string Converting = "unsafe class C { void M(ManagedExportedFunctions* m) { var p = (ExportedFunctionsPrefix*) m; } }";

		string documented = CSharpCode.BlankCommentsAndLiterals(Documented);
		string converting = CSharpCode.BlankCommentsAndLiterals(Converting);

		Assert.DoesNotMatch(Identifier(Prefix), documented);
		Assert.True(Identifier(Managed).IsMatch(converting) && Identifier(Prefix).IsMatch(converting));
		Assert.Single(PrefixPointerCast().Matches(converting));
	}

	private static IEnumerable<(string File, string Code)> ProductionCode()
	{
		return RepositoryRoot.EnumerateSourceFiles("*.cs")
			.Where(static file => file.StartsWith("libs/", StringComparison.Ordinal))
			.Order(StringComparer.Ordinal)
			.Select(static file => (file, CSharpCode.BlankCommentsAndLiterals(
				File.ReadAllText(RepositoryDocument.Absolute(file), Encoding.UTF8))));
	}

	private static Regex Identifier(string name)
	{
		return string.Equals(name, Managed, StringComparison.Ordinal) ? ManagedIdentifier() : PrefixIdentifier();
	}

	[GeneratedRegex(@"\bManagedExportedFunctions\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ManagedIdentifier();

	[GeneratedRegex(@"\bExportedFunctionsPrefix\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex PrefixIdentifier();

	[GeneratedRegex(@"\(\s*(?:[\w.]+\.)?ExportedFunctionsPrefix\s*\*\s*\)", RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex PrefixPointerCast();
}
