namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     Test vectors of the PR policy (audit register PR-CQ-34, shared-contracts §1.12). Every rule id appears in at least
///     one failing vector, and the titles of real pull requests of this repository are included.
/// </summary>
internal static class PullRequestPolicyCases
{
	public const string TitleLength = "TitleLength";
	public const string TitleNoTrailingPeriod = "TitleNoTrailingPeriod";
	public const string TitleNoConventionalPrefix = "TitleNoConventionalPrefix";
	public const string TitleStartsUppercase = "TitleStartsUppercase";
	public const string TitleImperative = "TitleImperative";
	public const string ChangelogEntry = "ChangelogEntry";

	/// <summary>Every rule, in the order the module reports them.</summary>
	public static readonly string[] Rules =
		[TitleLength, TitleNoTrailingPeriod, TitleNoConventionalPrefix, TitleStartsUppercase, TitleImperative, ChangelogEntry];

	/// <summary>A consumer-visible path: the CHANGELOG rule applies.</summary>
	public const string ScanningSource = "libs/CheatEngine.SDK.Engine/Scanning/A.cs";

	/// <summary>
	///     The checklist line planned for <c>.github/PULL_REQUEST_TEMPLATE.md</c> in the lot brief: it quotes the marker, and
	///     GitHub prefills every description with it, so it must never waive the rule.
	/// </summary>
	public const string PlannedTemplateChecklistLine =
		"- [ ] Consumer-visible changes are recorded under `[Unreleased]`, or the description contains `<!-- changelog: not-needed -->` with a reason";

	public static readonly PullRequestPolicyCase[] All =
	[
		new("imperative_title_with_changelog", "Add CodeQL analysis for C# and C/C++", ["libs/X/A.cs", "CHANGELOG.md"], []),
		// The open audit-remediation vehicle pull request (#86).
		new("vehicle_pr_title", "Remediate the 2026-09-22 audit and overhaul CI/CD", [".github/workflows/ci.yml"], []),
		new("revert_title", "Revert \"Remove unavailable Sonar CI integration\"", ["docs/README.md"], []),
		new("allowlisted_first_word", "Embed the SBOM in the package", [".github/x.yml"], []),
		new("allowlisted_s_ending_verb", "Focus the scan on committed files", [".github/x.yml"], []),
		new("exactly_72_characters", "Add " + new string('x', 68), ["README.md"], []),
		new("exactly_73_characters", "Add " + new string('x', 69), ["README.md"], [TitleLength]),
		// 72 text elements, 140 UTF-16 code units: the limit counts what a reader sees.
		new("combining_characters_count_as_one", "Add " + string.Concat(Enumerable.Repeat("é", 68)), ["README.md"], []),
		new("surrounding_whitespace_is_trimmed", "  Fix CI validation findings  ", ["README.md"], []),
		new("trailing_period", "Fix CI validation findings.", ["README.md"], [TitleNoTrailingPeriod]),
		new("conventional_prefix", "feat: add codeql", ["README.md"], [TitleNoConventionalPrefix, TitleStartsUppercase]),
		new("scoped_breaking_prefix", "Fix(ci)!: tidy", ["README.md"], [TitleNoConventionalPrefix]),
		// PR #83 used an area prefix, which the no-prefix rule also rejects.
		new("historical_area_prefix", "SDK: finalize runtime ownership and scan contracts", ["README.md"],
			[TitleNoConventionalPrefix]),
		new("past_tense", "Added CodeQL", ["README.md"], [TitleImperative]),
		new("gerund", "Adding CodeQL", ["README.md"], [TitleImperative]),
		new("third_person", "Adds CodeQL", ["README.md"], [TitleImperative]),
		new("wip_marker", "WIP audit work", ["README.md"], [TitleImperative]),
		new("lowercase_start", "fix CI", ["README.md"], [TitleStartsUppercase]),
		new("empty_title", "", ["README.md"], [TitleLength]),
		new("whitespace_title", "   ", ["README.md"], [TitleLength]),
		new("libs_change_without_changelog", "Fix AOB outcome", [ScanningSource], [ChangelogEntry]),
		new("waiver_marker", "Fix AOB outcome", [ScanningSource], [],
			"Internal rename only.\n\n<!-- changelog: not-needed -->"),
		new("waiver_marker_spacing", "Fix AOB outcome", [ScanningSource], [], "<!--changelog:not-needed-->"),
		// Descriptions edited on github.com end their lines with CR LF.
		new("waiver_marker_crlf_description", "Fix AOB outcome", [ScanningSource], [],
			"Internal rename only.\r\n\r\n<!-- changelog: not-needed -->\r\n"),
		new("waiver_marker_indented_up_to_three_spaces", "Fix AOB outcome", [ScanningSource], [], "   <!-- changelog: not-needed -->  "),
		new("waiver_marker_after_a_closed_fence", "Fix AOB outcome", [ScanningSource], [],
			"```text\nsample\n```\n<!-- changelog: not-needed -->"),
		new("waiver_marker_after_a_closed_comment", "Fix AOB outcome", [ScanningSource], [],
			"<!-- Describe the change.\nKeep it short. -->\nInternal rename only.\n<!-- changelog: not-needed -->"),
		new("waiver_marker_is_case_sensitive", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"<!-- CHANGELOG: NOT-NEEDED -->"),
		// Text that quotes the marker (the pull request template, CONTRIBUTING.md) never waives the rule.
		new("template_checklist_quotes_the_marker", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			PlannedTemplateChecklistLine),
		new("waiver_marker_in_a_code_span_line", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"`<!-- changelog: not-needed -->`"),
		new("waiver_marker_in_a_fenced_block", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"```md\n<!-- changelog: not-needed -->\n```"),
		new("waiver_marker_in_a_tilde_fence", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"~~~\n<!-- changelog: not-needed -->\n~~~"),
		new("waiver_marker_in_an_unclosed_fence", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"```\n<!-- changelog: not-needed -->"),
		new("waiver_marker_after_a_shorter_closing_fence", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"````\n```\n<!-- changelog: not-needed -->\n````"),
		new("waiver_marker_indented_as_code", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"Example:\n\n    <!-- changelog: not-needed -->"),
		new("waiver_marker_within_a_sentence", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"No consumer impact <!-- changelog: not-needed --> here."),
		new("waiver_marker_inside_a_longer_comment", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"<!-- To waive the rule, add the line\n<!-- changelog: not-needed -->\n-->"),
		new("waiver_marker_in_a_block_quote", "Fix AOB outcome", [ScanningSource], [ChangelogEntry],
			"> <!-- changelog: not-needed -->"),
		new("lock_file_only_under_libs", "Refresh the lock file", ["libs/CheatEngine.SDK.Lua/packages.lock.json"], []),
		new("native_readme_change", "Document the bridge exports", ["native/cheatengine-sdk-lua-bridge/README.md"],
			[ChangelogEntry]),
		new("build_assets_change", "Warn on x86 consumers", ["src/CheatEngine.SDK/build/CheatEngine.SDK.targets"],
			[ChangelogEntry]),
		new("analyzer_change", "Report the legacy registration pair",
			["analyzers/CheatEngine.SDK.Analyzers/Diagnostics/DiagnosticIds.cs"], [ChangelogEntry]),
		new("generator_change", "Emit global-qualified names",
			["source-generators/CheatEngine.SDK.SourceGenerators.LuaBindings/Emitter.cs"], [ChangelogEntry]),
		new("tests_and_workflows_only", "Repeat the threading tests weekly",
			["tests/CheatEngine.SDK.Lua.Tests/X.cs", ".github/workflows/ci.yml"], []),
		new("case_sensitive_paths", "Fix a sample", ["Libs/X.cs"], []),
		new("nested_changelog_does_not_count", "Fix AOB outcome", [ScanningSource, "docs/CHANGELOG.md"], [ChangelogEntry]),
		// PR #10: generated title, lock-file and central version changes.
		new("dependabot_exempt", "Bump the minor-and-patch group with 1 update",
			["libs/CheatEngine.SDK.Lua/packages.lock.json", "Directory.Packages.props"], [], Author: "dependabot[bot]"),
		new("dependabot_exempt_even_for_a_broken_title", "bump stuff.", [ScanningSource], [], Author: "dependabot[bot]"),
		new("dependabot_lookalike_login_is_not_exempt", "Bump xunit", [ScanningSource], [ChangelogEntry],
			Author: "dependabot")
	];

	/// <summary>The case names, for <c>[MemberData]</c>.</summary>
	public static TheoryData<string> Names
	{
		get
		{
			TheoryData<string> names = [];
			foreach (PullRequestPolicyCase policyCase in All)
			{
				names.Add(policyCase.Name);
			}

			return names;
		}
	}

	/// <summary>The case with the given name.</summary>
	public static PullRequestPolicyCase Get(string name)
	{
		foreach (PullRequestPolicyCase policyCase in All)
		{
			if (string.Equals(policyCase.Name, name, StringComparison.Ordinal))
			{
				return policyCase;
			}
		}

		throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown PR policy case.");
	}
}
