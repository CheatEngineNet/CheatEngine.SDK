using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>
///     Repository-specific values of the documentation integrity contract. The parser, the path resolver and the rules
///     read every value that differs between the SDK and the Client repository from here, so the design can be mirrored
///     by changing this class only.
/// </summary>
internal static partial class DocumentationConventions
{
	/// <summary>Timeout of every regular expression of the documentation tests.</summary>
	public const int RegexTimeoutMilliseconds = 1000;

	/// <summary>The GitHub <c>owner/name</c> of this repository.</summary>
	public const string RepositorySlug = "CheatEngineNet/CheatEngine.SDK";

	/// <summary>The branch whose absolute <c>blob</c>/<c>tree</c> links must resolve on the current tree.</summary>
	public const string MainBranch = "main";

	/// <summary>The documentation tree that commit <c>4020a32</c> removed and that is never restored.</summary>
	public const string RetiredTreePrefix = "documentations/";

	/// <summary>The folder of the pages rebuilt after the 2026-09-22 audit.</summary>
	public const string DocsFolder = "docs";

	/// <summary>The index of <see cref="DocsFolder" />.</summary>
	public const string DocsIndex = "docs/README.md";

	/// <summary>The line every page under <see cref="DocsFolder" /> carries (shared contract, section 7).</summary>
	public const string RecreatedHeader = "> Recreated 2026-09 from the audit, not the historical documentations/ tree.";

	/// <summary>Start of a placeholder status line; such a line must match <see cref="PlaceholderPattern" />.</summary>
	public const string PlaceholderPrefix = "Status: placeholder";

	/// <summary>
	///     Folder names skipped in addition to the build-output and tool-state folders that
	///     <c>RepositoryRoot.EnumerateSourceFiles</c> already skips. BenchmarkDotNet writes <c>*-report-github.md</c> files.
	/// </summary>
	public static readonly string[] ExtraExcludedSegments = ["BenchmarkDotNet.Artifacts", ".xmake", "node_modules"];

	/// <summary>
	///     Folder names that only ever hold build output or tool state. A link into one of them cannot resolve on GitHub,
	///     even when the folder exists on a developer's disk.
	/// </summary>
	public static readonly string[] UncommittedSegments =
	[
		"artifacts", "bin", "obj", ".git", ".idea", ".vs", "TestResults", "BenchmarkDotNet.Artifacts", ".xmake",
		"node_modules"
	];

	/// <summary>
	///     The only files allowed to name retired paths in text. <c>docs/README.md</c> lists them, as code spans and never as
	///     links, in its retired-documentation table.
	/// </summary>
	public static readonly string[] RetiredReferenceExemptions = [DocsIndex];

	/// <summary>
	///     The absolute links of the README packed into CheatEngine.SDK 1.0.0 (<c>git show v1.0.0:src/CheatEngine.SDK/README.md</c>,
	///     lines 72, 117 and 148). That README is published on nuget.org and can never change, so this list is frozen
	///     here rather than read from the current file (audit ADR-12, consequence on distributed links).
	/// </summary>
	public static readonly string[] PublishedPackageReadmeLinks =
	[
		"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine",
		"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/README.md",
		"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/LICENSE"
	];

	/// <summary>
	///     An absolute link back into this repository on <see cref="MainBranch" />. Group <c>rest</c> holds the path, the
	///     optional query and the optional fragment as written.
	/// </summary>
	public static readonly Regex SelfLinkPattern = new(
		"(?i:https://github\\.com/" + Regex.Escape(RepositorySlug) + ")/(?<kind>blob|tree)/" + Regex.Escape(MainBranch) +
		"/(?<rest>[^\\s)\\]\"'`>]*)",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
		TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds));

	/// <summary>
	///     A placeholder status line: the owning lot (<c>S-QUAL</c>, <c>S-CAT-A</c>, <c>DOCS-FINAL</c>, ...) and its wave
	///     (<c>V1</c> to <c>V4</c>, optionally <c>a</c> to <c>c</c>), separated from the prefix by an em dash (U+2014).
	/// </summary>
	[GeneratedRegex("^Status: placeholder \u2014 content arrives with (S-[A-Z]+(-[A-Z]+)*|DOCS-FINAL) \\((V[1-4][a-c]?)\\)$",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
	public static partial Regex PlaceholderPattern
	{
		get;
	}

	/// <summary>
	///     A reference to a retired page, forbidden in text outside <see cref="RetiredReferenceExemptions" />: a path under
	///     <see cref="RetiredTreePrefix" /> or the removed bridge audit page. A bare <c>documentations/</c> (the name of the
	///     tree) stays allowed.
	/// </summary>
	[GeneratedRegex("documentations/[A-Za-z]|cheatengine-sdk-lua-bridge/AUDIT\\.md", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	public static partial Regex RetiredReferencePattern
	{
		get;
	}

	/// <summary>
	///     Wording that claims complete coverage or universal support (audit A00-03, A20-14, F16 "no global percentage").
	///     Broader wording ("all public Lua", "every CE profile") stays a manual review item, because later pages are
	///     expected to state it negatively.
	/// </summary>
	[GeneratedRegex("(?<![0-9.,])100 ?%|complete coverage|full coverage|fully supported|fully compatible",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	public static partial Regex UniversalClaimPattern
	{
		get;
	}

	/// <summary>A negation that, earlier on the same line, turns a <see cref="UniversalClaimPattern" /> match into a denial.</summary>
	[GeneratedRegex("\\b(not|never|no|without)\\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
	public static partial Regex NegationPattern
	{
		get;
	}
}
