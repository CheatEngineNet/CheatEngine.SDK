using System.Security.Cryptography;
using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     The community and security documents (audit register PR-CQ-36, A22-43): <c>SECURITY.md</c>,
///     <c>CODE_OF_CONDUCT.md</c>, the informational <c>.github/CODEOWNERS</c> and the issue forms, whose compatibility
///     form captures the complete support tuple (audit ch.21 "the tuple to qualify", ch.22 Checkpoint F exit) and never
///     tells users to alter Cheat Engine's runtime configuration (ch.21 "CE runtime").
/// </summary>
public sealed partial class GovernanceDocumentTests
{
	private const string SecurityPolicy = "SECURITY.md";
	private const string CodeOfConduct = "CODE_OF_CONDUCT.md";
	private const string CodeOwners = ".github/CODEOWNERS";
	private const string IssueTemplates = ".github/ISSUE_TEMPLATE";
	private const string CompatibilityForm = ".github/ISSUE_TEMPLATE/compatibility.yml";
	private const string ChooserConfiguration = ".github/ISSUE_TEMPLATE/config.yml";

	private const string PrivateReportingUrl =
		"https://github.com/CheatEngineNet/CheatEngine.SDK/security/advisories/new";

	private const string LuaFixture = "native/cheat-engine/lua53-64.dll";

	private const string BridgeBinary =
		"native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll";

	/// <summary>Maintainers who may own paths; CODEOWNERS is informational (no required code-owner review).</summary>
	private static readonly string[] s_knownOwners = ["@AriusII", "@ShadowNineX"];

	/// <summary>The support tuple of the compatibility form: required ids, then optional ids.</summary>
	private static readonly string[] s_requiredTupleIds =
	[
		"sdk-version", "sdk-content-hash", "bridge-sha256", "ce-version", "ce-exe-sha256", "lua-dll-sha256",
		"runtimeconfig-sha256", "runtimeconfig-origin", "load-profile", "target-architecture", "evidence-level",
		"build-options", "os", "dotnet", "steps", "expected", "actual"
	];

	private static readonly string[] s_optionalTupleIds = ["bridge-fingerprint", "client-version", "logs"];

	[Fact]
	public void Security_policy_names_private_reporting_scope_response_and_supported_versions()
	{
		string text = RepositoryFile.ReadText(SecurityPolicy);

		Assert.Equal(
			[
				"Supported versions", "Reporting a vulnerability", "Scope", "Response", "Verifying releases",
				"Binary files in this repository"
			],
			Level2Headings(text));
		Assert.Contains(PrivateReportingUrl, text, StringComparison.Ordinal);
		Assert.Contains("Never report a vulnerability in a public issue", text, StringComparison.Ordinal);
		Assert.Contains("within 7 days", text, StringComparison.Ordinal);
		Assert.Contains("https://github.com/cheat-engine/cheat-engine", text, StringComparison.Ordinal);
		// The package identity a report must carry comes from the consumer's lock file (audit ADR-10).
		Assert.Contains("`contentHash`", text, StringComparison.Ordinal);
		Assert.Contains("packages.lock.json", text, StringComparison.Ordinal);
		Assert.Contains("[RELEASING.md](RELEASING.md)", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Security_policy_describes_the_committed_binaries_with_their_real_hash()
	{
		string policy = RepositoryFile.ReadText(SecurityPolicy);
		foreach (string binary in (string[]) [LuaFixture, BridgeBinary])
		{
			Assert.Contains($"`{binary}`", policy, StringComparison.Ordinal);
			Assert.True(RepositoryFile.ExistsWithExactCase(binary, out bool isDirectory) && !isDirectory,
				$"{SecurityPolicy} describes '{binary}', which is not in the repository.");
		}

		string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(RepositoryFile.FullPath(LuaFixture))));
		Assert.Contains($"`{actual}`", policy, StringComparison.Ordinal);
		Assert.Contains($"`{actual}`", RepositoryFile.ReadText("native/cheat-engine/README.md"),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Code_of_conduct_routes_reports_through_private_reporting_without_an_email_address()
	{
		string text = RepositoryFile.ReadText(CodeOfConduct);

		Assert.Contains("https://www.contributor-covenant.org/version/2/1/code_of_conduct.html", text,
			StringComparison.Ordinal);
		Assert.Contains(PrivateReportingUrl, text, StringComparison.Ordinal);
		Assert.DoesNotMatch(EmailAddress(), text);
	}

	[Fact]
	public void Codeowners_patterns_point_to_existing_paths_and_known_owners()
	{
		List<(string Pattern, string[] Owners)> rules = [];
		foreach (string rawLine in RepositoryFile.ReadLines(CodeOwners))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			string[] parts = line.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
			rules.Add((parts[0], parts[1..]));
		}

		Assert.NotEmpty(rules);
		Assert.Equal("*", rules[0].Pattern);
		foreach ((string pattern, string[] owners) in rules)
		{
			Assert.NotEmpty(owners);
			foreach (string owner in owners)
			{
				Assert.True(Array.IndexOf(s_knownOwners, owner) >= 0,
					$"{CodeOwners}: '{owner}' is not a known maintainer.");
			}

			// GitHub rejects "!" negation and "[ ]" ranges in CODEOWNERS.
			Assert.True(pattern.AsSpan().IndexOfAny("![]") < 0, $"{CodeOwners}: '{pattern}' uses unsupported syntax.");
			if (string.Equals(pattern, "*", StringComparison.Ordinal))
			{
				continue;
			}

			string path = pattern.Trim('/');
			Assert.True(RepositoryFile.ExistsWithExactCase(path, out bool isDirectory),
				$"{CodeOwners}: '{pattern}' matches nothing in the repository (exact case).");
			Assert.True(!pattern.EndsWith('/') || isDirectory,
				$"{CodeOwners}: '{pattern}' names a folder that is a file.");
		}
	}

	[Fact]
	public void Codeowners_exists_only_in_the_github_folder()
	{
		// GitHub uses the first of .github/, the root and docs/; a second file would be silently ignored.
		Assert.True(File.Exists(RepositoryFile.FullPath(CodeOwners)));
		Assert.False(File.Exists(RepositoryFile.FullPath("CODEOWNERS")));
		Assert.False(File.Exists(RepositoryFile.FullPath("docs/CODEOWNERS")));
	}

	[Fact]
	public void Compatibility_issue_form_requires_the_full_tuple()
	{
		Dictionary<string, YamlMappingNode> elements = ElementsById(YamlDocument.Load(CompatibilityForm));

		foreach (string id in s_requiredTupleIds)
		{
			Assert.True(elements.TryGetValue(id, out YamlMappingNode? element),
				$"{CompatibilityForm} has no '{id}' field.");
			Assert.True(IsRequired(element), $"{CompatibilityForm}: '{id}' must be required.");
		}

		foreach (string id in s_optionalTupleIds)
		{
			Assert.True(elements.TryGetValue(id, out YamlMappingNode? element),
				$"{CompatibilityForm} has no '{id}' field.");
			Assert.False(IsRequired(element), $"{CompatibilityForm}: '{id}' is optional.");
		}

		IReadOnlyList<string> levels =
			YamlDocument.Scalars(YamlDocument.Child(elements["evidence-level"], "attributes"), "options");
		foreach (string level in (string[]) ["(C0)", "(C1/C2)", "(C3)", "(C4)"])
		{
			Assert.Contains(levels, option => option.EndsWith(level, StringComparison.Ordinal));
		}

		IReadOnlyList<string> profiles =
			YamlDocument.Scalars(YamlDocument.Child(elements["load-profile"], "attributes"), "options");
		Assert.Contains(profiles,
			static option => option.Contains("ce-7.7.0.10621-x64-managed-hostfxr", StringComparison.Ordinal));
	}

	[Fact]
	public void Compatibility_form_never_presents_a_profile_as_qualified_or_supported()
	{
		// The support profile starts NotExecuted: a form option must not claim more (audit ch.20 publication criterion).
		foreach (string text in FormStrings(YamlDocument.Load(CompatibilityForm)))
		{
			foreach (string sentence in Sentences(text))
			{
				if (QualificationClaim().IsMatch(sentence) && !NegationPattern().IsMatch(sentence))
				{
					Assert.Fail($"{CompatibilityForm} claims support or qualification: '{sentence}'.");
				}
			}
		}
	}

	[Fact]
	public void Issue_form_element_ids_are_unique_and_valid()
	{
		List<string> forms = IssueForms();
		Assert.Contains(CompatibilityForm, forms, StringComparer.Ordinal);
		Assert.Contains(".github/ISSUE_TEMPLATE/bug_report.yml", forms, StringComparer.Ordinal);

		foreach (string path in forms)
		{
			YamlDocument form = YamlDocument.Load(path);
			foreach (string key in (string[]) ["name", "description"])
			{
				Assert.False(string.IsNullOrWhiteSpace(YamlDocument.Scalar(form.Root, key)), $"{path} has no {key}.");
			}

			HashSet<string> ids = new(StringComparer.Ordinal);
			IReadOnlyList<YamlMappingNode> body = YamlDocument.Mappings(form.Root, "body");
			Assert.NotEmpty(body);
			foreach (YamlMappingNode element in body)
			{
				string type = YamlDocument.Scalar(element, "type") ?? "";
				Assert.Contains(type, (string[]) ["markdown", "input", "textarea", "dropdown", "checkboxes"],
					StringComparer.Ordinal);
				string? id = YamlDocument.Scalar(element, "id");
				if (string.Equals(type, "markdown", StringComparison.Ordinal))
				{
					continue;
				}

				Assert.True(id is not null && ElementId().IsMatch(id),
					$"{path}: a {type} element has no valid id ('{id}').");
				Assert.True(ids.Add(id), $"{path}: the id '{id}' is used twice.");
				YamlNode? attributes = YamlDocument.Child(element, "attributes");
				Assert.False(string.IsNullOrWhiteSpace(YamlDocument.Scalar(attributes, "label")),
					$"{path}: '{id}' has no label.");
				if (string.Equals(type, "dropdown", StringComparison.Ordinal))
				{
					IReadOnlyList<string> options = YamlDocument.Scalars(attributes, "options");
					Assert.NotEmpty(options);
					Assert.Equal(options.Count, new HashSet<string>(options, StringComparer.Ordinal).Count);
				}
			}

			// Forms are public: no local path, and every link to this repository resolves on main.
			Assert.DoesNotMatch(AbsoluteLocalPath(), form.Text);
			foreach (Match link in SelfLink().Matches(form.Text))
			{
				Assert.True(TryCheckSelfLink(link, out string? problem),
					$"{path}: {link.Value} does not resolve ({problem}).");
			}
		}
	}

	[Fact]
	public void Issue_forms_disable_blank_issues_and_link_private_reporting()
	{
		YamlDocument chooser = YamlDocument.Load(ChooserConfiguration);

		Assert.Equal("false", YamlDocument.Scalar(chooser.Root, "blank_issues_enabled"));
		List<string> urls = [];
		foreach (YamlMappingNode link in YamlDocument.Mappings(chooser.Root, "contact_links"))
		{
			foreach (string key in (string[]) ["name", "url", "about"])
			{
				Assert.False(string.IsNullOrWhiteSpace(YamlDocument.Scalar(link, key)),
					$"{ChooserConfiguration}: a contact link has no {key}.");
			}

			string url = YamlDocument.Scalar(link, "url")!;
			Assert.StartsWith("https://", url, StringComparison.Ordinal);
			urls.Add(url);
		}

		Assert.Contains(PrivateReportingUrl, urls, StringComparer.Ordinal);
		Assert.Contains("https://github.com/CheatEngineNet/CheatEngine.SDK/discussions", urls, StringComparer.Ordinal);
	}

	[Fact]
	public void Issue_forms_never_instruct_editing_the_cheat_engine_runtime_configuration()
	{
		// Audit ch.21: editing a global CE runtime configuration is never a harmless step. A sentence may mention the file
		// and an edit only to rule the edit out.
		List<string> offenders = [];
		foreach (string path in IssueForms())
		{
			foreach (string text in FormStrings(YamlDocument.Load(path)))
			{
				foreach (string sentence in Sentences(text))
				{
					if (sentence.Contains("runtimeconfig", StringComparison.OrdinalIgnoreCase)
						&& EditInstruction().IsMatch(sentence)
						&& !NegationPattern().IsMatch(sentence))
					{
						offenders.Add($"{path}: '{sentence}'");
					}
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"Issue forms must never ask users to edit ce.runtimeconfig.json: {string.Join("; ", offenders)}");
	}

	/// <summary>The <c>## </c> (ATX level-2) headings of a Markdown document, outside fenced code blocks, in order.</summary>
	private static List<string> Level2Headings(string text)
	{
		List<string> headings = [];
		bool inFence = false;
		foreach (string rawLine in text.ReplaceLineEndings("\n").Split('\n'))
		{
			string line = rawLine.TrimEnd();
			string trimmedStart = line.TrimStart();
			if (trimmedStart.StartsWith("```", StringComparison.Ordinal) ||
				trimmedStart.StartsWith("~~~", StringComparison.Ordinal))
			{
				inFence = !inFence;
				continue;
			}

			if (!inFence && trimmedStart.StartsWith("## ", StringComparison.Ordinal))
			{
				headings.Add(trimmedStart[3..].Trim());
			}
		}

		return headings;
	}

	private static bool TryCheckSelfLink(Match link, out string? problem)
	{
		string rest = link.Groups["rest"].Value.Split('#')[0].Split('?')[0].TrimEnd('/');
		if (rest.Length == 0 || RepositoryFile.ExistsWithExactCase(rest, out _))
		{
			problem = null;
			return true;
		}

		problem = $"'{rest}' does not exist";
		return false;
	}

	private static List<string> IssueForms()
	{
		List<string> forms = [];
		foreach (string file in Directory.EnumerateFiles(RepositoryFile.FullPath(IssueTemplates), "*.yml"))
		{
			string relative = IssueTemplates + "/" + Path.GetFileName(file);
			if (!string.Equals(relative, ChooserConfiguration, StringComparison.Ordinal))
			{
				forms.Add(relative);
			}
		}

		forms.Sort(StringComparer.Ordinal);
		return forms;
	}

	private static Dictionary<string, YamlMappingNode> ElementsById(YamlDocument form)
	{
		Dictionary<string, YamlMappingNode> elements = new(StringComparer.Ordinal);
		foreach (YamlMappingNode element in YamlDocument.Mappings(form.Root, "body"))
		{
			string? id = YamlDocument.Scalar(element, "id");
			if (id is not null)
			{
				elements[id] = element;
			}
		}

		return elements;
	}

	private static bool IsRequired(YamlMappingNode element)
	{
		return string.Equals(YamlDocument.Scalar(YamlDocument.Child(element, "validations"), "required"), "true",
			StringComparison.Ordinal);
	}

	/// <summary>Every user-visible string of a form: its description and each element's texts and options.</summary>
	private static List<string> FormStrings(YamlDocument form)
	{
		List<string> strings = [];
		foreach (string key in (string[]) ["name", "description", "title"])
		{
			strings.AddRange(YamlDocument.Scalars(form.Root, key));
		}

		foreach (YamlMappingNode element in YamlDocument.Mappings(form.Root, "body"))
		{
			YamlNode? attributes = YamlDocument.Child(element, "attributes");
			foreach (string key in (string[]) ["label", "description", "placeholder", "value", "options"])
			{
				strings.AddRange(YamlDocument.Scalars(attributes, key));
			}
		}

		return strings;
	}

	private static IEnumerable<string> Sentences(string text)
	{
		foreach (string sentence in SentenceBoundary().Split(text))
		{
			string trimmed = sentence.Trim();
			if (trimmed.Length != 0)
			{
				yield return trimmed;
			}
		}
	}

	[GeneratedRegex(@"(?<=[.!?;])\s+|\r?\n", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex SentenceBoundary();

	[GeneratedRegex(@"\b(edit|modify|change|replace|overwrite)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
		1000)]
	private static partial Regex EditInstruction();

	[GeneratedRegex(@"\b(supported|qualified|compatible)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
		1000)]
	private static partial Regex QualificationClaim();

	[GeneratedRegex(@"\b(not|never|no|without)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
		1000)]
	private static partial Regex NegationPattern();

	[GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ElementId();

	[GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex EmailAddress();

	/// <summary>A drive path, a user-profile folder or a <c>file:</c> URI: never valid in a public issue form.</summary>
	[GeneratedRegex(@"[A-Za-z]:\\|\\Users\\|/home/|/Users/|file://", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex AbsoluteLocalPath();

	/// <summary>An absolute link back into this repository on <c>main</c>. Group <c>rest</c> is the path as written.</summary>
	[GeneratedRegex(
		@"(?i:https://github\.com/CheatEngineNet/CheatEngine\.SDK)/(?:blob|tree)/main/(?<rest>[^\s)\]""'`>]*)",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex SelfLink();
}
