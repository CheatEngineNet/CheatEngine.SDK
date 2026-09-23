using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.HostContract;

/// <summary>
///     WI-2 (F04, A23-F04-3, A08-06, A20-Q19-1): the SDK never claims a runtime or load-context cardinality it has not
///     qualified. "One binding per loaded assembly instance" is a fact this SDK copy owns; "one per plugin" is a host
///     loader fact that only Q09 evidence can establish.
/// </summary>
public sealed class ConcurrencyContractDocumentationTests
{
	// This test's own source quotes the banned phrases verbatim (as the pattern it looks for and in this remark), so
	// it is excluded from its own scan; every other file under libs/ and tests/ is still checked.
	private const string SelfPath =
		"tests/CheatEngine.SDK.Repository.Tests/HostContract/ConcurrencyContractDocumentationTests.cs";

	private static readonly Regex UnqualifiedCardinalityClaim = new(
		@"\(one per plugin\)|one load context hosts one plugin|hosted by this assembly load context",
		RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	[Fact]
	public void Sources_and_readmes_never_claim_one_runtime_or_load_context_per_plugin()
	{
		List<string> offenders = [];
		foreach (string relative in RepositoryRoot.EnumerateSourceFiles("*.cs")
			         .Concat(RepositoryRoot.EnumerateSourceFiles("*.md"))
			         .Where(static path => (path.StartsWith("libs/", StringComparison.Ordinal)
			                                || path.StartsWith("tests/", StringComparison.Ordinal))
			                               && !string.Equals(path, SelfPath, StringComparison.Ordinal))
			         .Distinct(StringComparer.Ordinal)
			         .Order(StringComparer.Ordinal))
		{
			string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, relative));
			foreach (Match match in UnqualifiedCardinalityClaim.Matches(text))
			{
				offenders.Add($"{relative}: '{match.Value}'");
			}
		}

		Assert.True(offenders.Count == 0,
			"Unqualified plugin-cardinality claim(s):" + Environment.NewLine +
			string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void Hosting_readme_publishes_the_ADR_07_concurrency_contract()
	{
		string readme =
			File.ReadAllText(Path.Combine(RepositoryRoot.Path, "libs", "CheatEngine.SDK.Hosting", "README.md"));
		Assert.Contains("Threading and Lua concurrency contract (ADR-07)", readme, StringComparison.Ordinal);
		int sectionStart = readme.IndexOf("Threading and Lua concurrency contract (ADR-07)", StringComparison.Ordinal);
		string section = readme[sectionStart..];

		Assert.Contains("CESDK5001", section, StringComparison.Ordinal);
		Assert.Contains("ADR-07", section, StringComparison.Ordinal);
		Assert.Contains("Q19", section, StringComparison.Ordinal);
		Assert.Contains("pluginCS", section, StringComparison.Ordinal);
		Assert.Contains("processMessages", section, StringComparison.Ordinal);
	}

	[Fact]
	public void Lua_readme_states_that_admission_is_not_a_heap_lock()
	{
		string readme = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "libs", "CheatEngine.SDK.Lua", "README.md"));
		Assert.Contains("not", readme, StringComparison.Ordinal);
		Assert.True(
			readme.Contains("not a process-wide Lua mutex", StringComparison.Ordinal)
			|| readme.Contains("not a heap lock", StringComparison.Ordinal)
			|| readme.Contains("never the shared Lua heap", StringComparison.Ordinal),
			"The Lua README must state that admission protects only this SDK copy's transitions, never the shared Lua heap.");
	}
}
