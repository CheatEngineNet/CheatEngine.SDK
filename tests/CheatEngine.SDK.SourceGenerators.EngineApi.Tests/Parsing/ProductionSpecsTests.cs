using CheatEngine.SDK.SourceGenerators.EngineApi.Model;
using CheatEngine.SDK.SourceGenerators.EngineApi.Parsing;
using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Parsing;

/// <summary>
///     The committed production specs, parsed as the generator parses them (audit F10, A23-F10-1, A06-14, AX06-18,
///     SRC02-02): every spec is valid and declares the CE 7.7 contract with the pinned <c>celua.txt</c> digest, the wired
///     specs have entries, the unwired specs with entries are the reviewed reservations, and no spec claims main-thread
///     affinity without evidence.
/// </summary>
public sealed class ProductionSpecsTests
{
	// SHA-256 of the installed CE 7.7.0.10621 celua.txt every production spec cites (shared-contracts, support profile).
	private const string CeluaSha256 = "AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00";

	// Unwired specs with entries, each reviewed: a wired spec may still be listed, so wiring one needs no edit here.
	private static readonly HashSet<string> ReviewedUnwiredSpecs = new(StringComparer.Ordinal)
	{
		"allocation-protection.cheatengine-sdk-api.txt",
		"modules-symbols-regions.cheatengine-sdk-api.txt",
		"runtime-capabilities.cheatengine-sdk-api.txt"
	};

	[Fact]
	public void Every_production_spec_parses_without_issues()
	{
		Assert.Equal(7, ProductionSpecs.All.Count);
		foreach ((string fileName, string text) in ProductionSpecs.All)
		{
			SpecFileModel spec = SpecFileParser.Parse(fileName, text);

			Assert.True(spec.Issues.IsEmpty,
				fileName + ": " + string.Join(" | ", spec.Issues.AsImmutableArray().Select(static i => i.Line + ": " + i.Message)));
			Assert.NotEqual(string.Empty, spec.TypeName, StringComparer.Ordinal);
		}
	}

	[Fact]
	public void Every_production_spec_declares_the_ce77_contract()
	{
		foreach ((string fileName, string text) in ProductionSpecs.All)
		{
			SpecFileModel spec = SpecFileParser.Parse(fileName, text);

			SpecFileContract contract = Assert.IsType<SpecFileContract>(spec.Contract);
			Assert.Equal("7.7.0.10621", contract.MinimumCheatEngineVersion);
			Assert.Equal("x64", contract.Architecture);
			foreach (SpecCallModel call in spec.Calls)
			{
				Assert.NotNull(call.Contract);
				Assert.False(string.IsNullOrEmpty(call.Contract.NilSemantics), fileName + " " + call.Call.MethodName);
			}
		}
	}

	[Fact]
	public void Every_production_spec_cites_the_pinned_celua_sha256()
	{
		foreach ((string fileName, string text) in ProductionSpecs.All)
		{
			SpecFileContract contract = Assert.IsType<SpecFileContract>(SpecFileParser.Parse(fileName, text).Contract);

			Assert.StartsWith("ExactInstalledFile: CE 7.7.0.10621 celua.txt", contract.Provenance, StringComparison.Ordinal);
			Assert.EndsWith("SHA-256 " + CeluaSha256, contract.Provenance, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Every_wired_spec_has_entries_and_no_issue()
	{
		IReadOnlySet<string> wired = ProductionSpecs.WiredFileNames();

		Assert.Contains("memory-scalars.cheatengine-sdk-api.txt", wired);
		foreach (string fileName in wired)
		{
			SpecFileModel spec = SpecFileParser.Parse(fileName, ProductionSpecs.Text(fileName));

			Assert.True(spec.Issues.IsEmpty, fileName);
			Assert.False(spec.Calls.IsEmpty, fileName + " is wired but generates nothing.");
		}
	}

	[Fact]
	public void Unwired_specs_with_entries_are_reviewed_reservations()
	{
		IReadOnlySet<string> wired = ProductionSpecs.WiredFileNames();
		foreach ((string fileName, string text) in ProductionSpecs.All)
		{
			SpecFileModel spec = SpecFileParser.Parse(fileName, text);
			if (wired.Contains(fileName) || spec.Calls.IsEmpty)
			{
				continue;
			}

			Assert.True(ReviewedUnwiredSpecs.Contains(fileName),
				fileName + " declares entries but is neither wired into CheatEngine.SDK.Engine nor a reviewed reservation.");
		}
	}

	[Fact]
	public void Production_specs_never_claim_main_thread_affinity_without_evidence()
	{
		foreach ((string fileName, string text) in ProductionSpecs.All)
		{
			SpecFileContract contract = Assert.IsType<SpecFileContract>(SpecFileParser.Parse(fileName, text).Contract);

			Assert.True(contract.ThreadAffinity is "unknown" or "any",
				fileName + " claims thread '" + contract.ThreadAffinity + "' without CE 7.7 evidence (audit SRC02-02).");
		}
	}
}
