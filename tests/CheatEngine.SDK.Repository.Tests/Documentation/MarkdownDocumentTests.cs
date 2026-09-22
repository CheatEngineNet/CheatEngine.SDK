namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>
///     Self-tests of the Markdown parser and of the documentation rules on in-memory pages. They prove that each gate of
///     <see cref="DocumentationIntegrityTests" /> fails on the regression it exists for: a pull request that adds a broken
///     link, a wrong-case path, a local path or a retired reference fails CI.
/// </summary>
public sealed class MarkdownDocumentTests
{
	private const string SyntheticPage = "tests/CheatEngine.SDK.Repository.Tests/Synthetic.md";

	[Fact]
	public void Slug_follows_github_rules_for_punctuation_dots_and_duplicates()
	{
		MarkdownDocument document = MarkdownDocument.Parse(string.Join('\n',
			"# Recipe \u00B7 Structure definitions",
			"## [1.0.0] - 2026-09-20",
			"## 2. Register on enable, unregister on disable",
			"## Install and build settings",
			"### `Owned<T>`",
			"## Install and build settings",
			"## Install and build settings ##",
			"## __init__, snake_case and *emphasis*",
			"```markdown",
			"## Not a heading: fenced",
			"```"));

		string[] anchors = new string[document.Headings.Count];
		for (int i = 0; i < anchors.Length; i++)
		{
			anchors[i] = document.Headings[i].Anchor;
		}

		Assert.Equal(
			[
				"recipe--structure-definitions",
				"100---2026-09-20",
				"2-register-on-enable-unregister-on-disable",
				"install-and-build-settings",
				"ownedt",
				"install-and-build-settings-1",
				"install-and-build-settings-2",
				"init-snake_case-and-emphasis"
			],
			anchors);
		Assert.Contains("install-and-build-settings-2", document.Anchors);
		Assert.DoesNotContain("not-a-heading-fenced", document.Anchors);
	}

	[Theory]
	[InlineData("\n")]
	[InlineData("\r\n")]
	public void Multi_line_link_text_reports_the_line_of_its_target(string lineEnding)
	{
		MarkdownDocument document = MarkdownDocument.Parse(string.Join(lineEnding,
			"# Probe",
			"",
			"- No live test is invoked by `dotnet test`.",
			"",
			"Detailed result templates and evidence rules live in [",
			"`documentations/CheatEngine.SDK/live-probes`](../../documentations/CheatEngine.SDK/live-probes/README.md).",
			"The [native",
			"fixture",
			"README](native/cheat-engine/README.md) spans three lines."));

		Assert.Equal(
			[
				new MarkdownLink(MarkdownLinkKind.Inline, "../../documentations/CheatEngine.SDK/live-probes/README.md", 6),
				new MarkdownLink(MarkdownLinkKind.Inline, "native/cheat-engine/README.md", 9)
			],
			document.Links);
	}

	[Fact]
	public void Links_inside_fences_code_spans_and_html_comments_are_ignored()
	{
		MarkdownDocument document = MarkdownDocument.Parse("""
			[kept](first.md)

			```text
			[fenced](fenced.md)
			```

			~~~~
			```
			[still fenced](tilde.md)
			~~~~

			- A list item:

			   ```lua
			   print("[indented fence](indented.md)")
			   ```

			Inline `[code](code.md)` and ``[double `tick`](double.md)`` spans.
			An escaped \[bracket](escaped.md) is text, and so is <!-- [inline](inline.md) --> a comment.

			<!--
			[block comment](block-comment.md)

			[still in the comment](block-comment-2.md)
			-->
			A lone ` backtick hides nothing: [kept too](second.md).
			""");

		string[] targets = new string[document.Links.Count];
		for (int i = 0; i < targets.Length; i++)
		{
			targets[i] = document.Links[i].Target;
		}

		Assert.Equal(["first.md", "second.md"], targets);
		Assert.True(document.IsInFencedCode(4));
		Assert.True(document.IsInFencedCode(9));
		Assert.False(document.IsInFencedCode(1));
	}

	[Fact]
	public void Reference_definitions_html_attributes_and_badge_images_are_extracted()
	{
		MarkdownDocument document = MarkdownDocument.Parse("""
			[![Build](https://img.shields.io/badge/build-passing.svg)](https://github.com/CheatEngineNet/CheatEngine.SDK/actions)
			[![MIT license](https://img.shields.io/badge/license-MIT.svg)](LICENSE)
			[titled](README.md "The title") and [angled](<docs/with%20space.md>) and [escaped](docs/a\_b.md)

			<p align="center"><a href="CONTRIBUTING.md"><img src="docs/logo.png" alt="logo"></a></p>
			<a id="custom-anchor"></a><a name="named-anchor"></a>
			<span data-src="not-a-link.md" title="x">text</span>

			[Unreleased]: https://github.com/CheatEngineNet/CheatEngine.SDK/compare/v1.0.0...HEAD
			[local]: <docs/README.md> "Title"
			[^1]: A footnote, not a link definition.
			""");

		List<string> found = [];
		foreach (MarkdownLink link in document.Links)
		{
			found.Add($"{link.Line} {link.Kind} {link.Target}");
		}

		found.Sort(StringComparer.Ordinal);
		Assert.Equal(
			[
				"1 Image https://img.shields.io/badge/build-passing.svg",
				"1 Inline https://github.com/CheatEngineNet/CheatEngine.SDK/actions",
				"10 ReferenceDefinition docs/README.md",
				"2 Image https://img.shields.io/badge/license-MIT.svg",
				"2 Inline LICENSE",
				"3 Inline README.md",
				"3 Inline docs/a_b.md",
				"3 Inline docs/with%20space.md",
				"5 HtmlAttribute CONTRIBUTING.md",
				"5 HtmlAttribute docs/logo.png",
				"9 ReferenceDefinition https://github.com/CheatEngineNet/CheatEngine.SDK/compare/v1.0.0...HEAD"
			],
			found);
		Assert.Contains("custom-anchor", document.Anchors);
		Assert.Contains("named-anchor", document.Anchors);
		Assert.Equal(new LinkTarget(LinkTargetKind.Relative, "docs/with space.md", null),
			LinkTarget.Parse("docs/with%20space.md"));
	}

	[Theory]
	[InlineData("Clone into D:\\CheatEngine\\x before building.", true)]
	[InlineData("See C:/Users/a/project for the layout.", true)]
	[InlineData("Open file:///c:/x in a browser.", true)]
	[InlineData("It lives in /home/a/project.", true)]
	[InlineData("It lives in /Users/a/project.", true)]
	[InlineData("```text\nD:\\wt\\s-doc\n```", true)]
	[InlineData("Logs go to `%APPDATA%\\TrainerLog\\plugin.log`.", false)]
	[InlineData("See https://learn.microsoft.com/dotnet and mailto:someone@example.com.", false)]
	[InlineData("The analyzer ships in `analyzers/dotnet/cs`; tests load `native/lua53-64.dll`.", false)]
	public void Absolute_drive_and_user_profile_paths_are_detected_but_environment_placeholders_are_not(string markdown,
		bool isLocalPath)
	{
		List<string> offenders = DocumentationRules.LocalPaths(SyntheticPage, MarkdownDocument.Parse(markdown));

		Assert.Equal(isLocalPath, offenders.Count > 0);
	}

	[Fact]
	public void Broken_exact_case_target_is_reported()
	{
		MarkdownDocument document = MarkdownDocument.Parse(
			"[wrong case](readme.md), [right case](README.md), [missing](missing.md) and [folder](../CheatEngine.SDK.Repository.Tests/)");

		List<string> offenders = DocumentationRules.BrokenRelativeLinks(SyntheticPage, document);

		Assert.Equal(2, offenders.Count);
		Assert.Contains(offenders, static offender =>
			offender.Contains("readme.md (", StringComparison.Ordinal)
			&& offender.Contains("differs in case from 'tests/CheatEngine.SDK.Repository.Tests/README.md'",
				StringComparison.Ordinal));
		Assert.Contains(offenders, static offender =>
			offender.Contains("missing.md (", StringComparison.Ordinal)
			&& offender.Contains("does not exist", StringComparison.Ordinal));
		Assert.False(RepositoryPaths.ExistsWithExactCase("tests/CheatEngine.SDK.Repository.Tests/readme.md"));
		Assert.True(RepositoryPaths.ExistsWithExactCase("tests/CheatEngine.SDK.Repository.Tests/README.md"));
	}

	[Fact]
	public void Targets_that_climb_above_the_root_or_point_into_build_output_are_rejected()
	{
		MarkdownDocument document = MarkdownDocument.Parse(
			"[outside](../../outside.md) [output](../artifacts/bin/x.dll) [bin](../src/CheatEngine.SDK/bin/Debug/x.dll) " +
			"[backslash](..\\README.md) [rooted](/LICENSE) [folder](../docs/) [query](../README.md?plain=1)");

		List<string> offenders = DocumentationRules.BrokenRelativeLinks("docs/Synthetic.md", document);

		Assert.Equal(4, offenders.Count);
		Assert.Contains(offenders, static offender => offender.Contains("climbs above the repository root", StringComparison.Ordinal));
		Assert.Contains(offenders, static offender => offender.Contains("points into 'artifacts/'", StringComparison.Ordinal));
		Assert.Contains(offenders, static offender => offender.Contains("points into 'bin/'", StringComparison.Ordinal));
		Assert.Contains(offenders, static offender => offender.Contains("as a path separator", StringComparison.Ordinal));
	}

	[Fact]
	public void Anchor_links_are_checked_against_headings_and_explicit_anchors()
	{
		MarkdownDocument document = MarkdownDocument.Parse("""
			# Synthetic

			## Retired documentation

			<a id="explicit"></a>

			[same page](#retired-documentation), [explicit](#explicit), [misspelled](#retired-docs),
			[published anchor](../tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine),
			[folder readme](../tests/CheatEngine.SDK.LivePlugin/#run-it-in-cheat-engine),
			[missing anchor](../tests/CheatEngine.SDK.LivePlugin/README.md#no-such-heading) and
			[source line](../LICENSE#L1).
			""");

		List<string> offenders = DocumentationRules.BrokenAnchors("docs/Synthetic.md", document);

		Assert.Equal(2, offenders.Count);
		Assert.Contains(offenders, static offender => offender.Contains("'#retired-docs' in 'docs/Synthetic.md'", StringComparison.Ordinal));
		Assert.Contains(offenders, static offender =>
			offender.Contains("'#no-such-heading' in 'tests/CheatEngine.SDK.LivePlugin/README.md'", StringComparison.Ordinal));
	}

	[Fact]
	public void Retired_tree_references_are_reported_outside_the_docs_index()
	{
		MarkdownDocument document = MarkdownDocument.Parse(
			"[old](../documentations/CheatEngine.SDK/SOURCES.md), `native/cheatengine-sdk-lua-bridge/AUDIT.md`, " +
			"and the bare name `documentations/`.");
		MarkdownDocument index = MarkdownDocument.Parse(
			"| `documentations/CheatEngine.SDK/SOURCES.md` | [support profile](qualification/support-profile.md) |");
		MarkdownDocument linkingIndex = MarkdownDocument.Parse("[old](../documentations/engineering/backlog.json)");

		Assert.Equal(3, DocumentationRules.RetiredReferences("exemples/Synthetic.md", document).Count);
		Assert.Empty(DocumentationRules.RetiredReferences(DocumentationConventions.DocsIndex, index));
		Assert.Single(DocumentationRules.RetiredReferences(DocumentationConventions.DocsIndex, linkingIndex));
	}

	[Fact]
	public void Absolute_links_to_this_repository_on_main_are_checked_and_templates_are_skipped()
	{
		const string Blob = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/";

		Assert.True(DocumentationRules.TryCheckSelfLink(Blob + "LICENSE", out string? problem));
		Assert.Null(problem);
		Assert.True(DocumentationRules.TryCheckSelfLink(Blob + "license", out problem));
		Assert.NotNull(problem);
		Assert.True(DocumentationRules.TryCheckSelfLink(
			Blob + "tests/CheatEngine.SDK.LivePlugin/README.md#no-such-heading", out problem));
		Assert.NotNull(problem);
		Assert.True(DocumentationRules.TryCheckSelfLink(
			"https://github.com/CheatEngineNet/CheatEngine.SDK/tree/main/docs", out problem));
		Assert.Null(problem);
		Assert.False(DocumentationRules.TryCheckSelfLink(Blob + "analyzers/docs/<ID>.md", out _));
		Assert.False(DocumentationRules.TryCheckSelfLink(
			"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/v1.0.0/LICENSE", out _));
		Assert.Empty(DocumentationRules.BrokenSelfLinks(SyntheticPage,
			MarkdownDocument.Parse("Read the [license](" + Blob + "LICENSE) or " + Blob + "LICENSE.")));
	}

	[Theory]
	[InlineData("The SDK is 100 % compatible with Cheat Engine.", true)]
	[InlineData("Complete coverage of the public Lua surface.", true)]
	[InlineData("Every profile is fully supported.", true)]
	[InlineData("It is fully compatible with CE 7.7.", true)]
	[InlineData("The NativeAOT plugin route is not fully supported.", false)]
	[InlineData("No 100% claim is made before the catalogue denominator exists.", false)]
	[InlineData("```text\n100 % inside a fenced sample\n```", false)]
	public void Universal_claims_are_reported_unless_a_negation_precedes_them(string markdown, bool isClaim)
	{
		List<string> offenders = DocumentationRules.UniversalClaims(SyntheticPage, MarkdownDocument.Parse(markdown));

		Assert.Equal(isClaim, offenders.Count > 0);
	}

	[Theory]
	[InlineData("Status: placeholder \u2014 content arrives with S-QUAL (V1)", true)]
	[InlineData("Status: placeholder \u2014 content arrives with S-CAT-A (V2)", true)]
	[InlineData("Status: placeholder \u2014 content arrives with DOCS-FINAL (V4c)", true)]
	[InlineData("Status: placeholder - content arrives with S-QUAL (V1)", false)]
	[InlineData("Status: placeholder \u2014 content arrives later", false)]
	[InlineData("Status: placeholder \u2014 content arrives with S-QUAL (V5)", false)]
	[InlineData("Status: placeholder \u2014 content arrives with S-QUAL (V1).", false)]
	public void Placeholder_lines_must_name_their_lot_and_wave(string line, bool isValid)
	{
		List<string> offenders = DocumentationRules.MalformedPlaceholders("docs/Synthetic.md", MarkdownDocument.Parse(line));

		Assert.Equal(isValid, offenders.Count == 0);
	}
}
