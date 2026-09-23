namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     Vectors for the pure decisions of the scheduled health workflow (<c>eng/ci/health/HealthCheck.psm1</c>): release
///     tag selection, bridge drift classification, SDK canary selection, report parsing, link verdicts and the health
///     issue (audit register PR-CQ-55, PR-CQ-30, A21-25).
/// </summary>
internal static class HealthCheckCases
{
	public const string ModulePath = "eng/ci/health/HealthCheck.psm1";

	public const string HashA = "da08c2ba03019da3a8c432ef061d5d6133fd2169ba3a6a8e9ac903353856d994";
	public const string HashB = "5c9923867989efcd256b643fdff61ed2fdfa1ee7133311bd89395e25d6d5cf77";
	public const string HashC = "039b03f62f57aa9d1ea20f006c9d917988bf0c23d166cb88c68ada30477e0d7c";

	/// <summary>The v1.0.0 bridge fingerprint (shared-contracts §2.4).</summary>
	public const string Fingerprint =
		"8a63e00c7dd941212e7ef8c13d8c97f73142c5154bfbe5dbc5459e7131bb789b:2871368515be4c6fd235e49e793d5557e7c50229fcc8fbfd903efd39f9b754a8";

	public const string RunUrl = "https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/123456789";

	public const string Trx = """
		<?xml version="1.0" encoding="utf-8"?>
		<TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
		  <ResultSummary outcome="Failed">
		    <Counters total="12" executed="11" passed="10" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" notExecuted="1" />
		  </ResultSummary>
		</TestRun>
		""";

	public const string VulnerableReport = """
		{
		  "version": 1,
		  "parameters": "--vulnerable --include-transitive",
		  "problems": [ { "project": "C:/repo/tests/X.Tests/X.Tests.csproj", "level": "warning", "text": "No assets file." } ],
		  "sources": [ "https://api.nuget.org/v3/index.json" ],
		  "projects": [
		    { "path": "C:/repo/libs/A/A.csproj" },
		    {
		      "path": "C:/repo/tests/B.Tests/B.Tests.csproj",
		      "frameworks": [
		        {
		          "framework": "net10.0",
		          "topLevelPackages": [
		            { "id": "Contoso.Direct", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0",
		              "vulnerabilities": [ { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-aaaa-bbbb-cccc" } ] }
		          ],
		          "transitivePackages": [
		            { "id": "Contoso.Transitive", "resolvedVersion": "2.1.0",
		              "vulnerabilities": [
		                { "severity": "Low", "advisoryurl": "https://github.com/advisories/GHSA-1111-2222-3333" },
		                { "severity": "Moderate", "advisoryurl": "https://github.com/advisories/GHSA-4444-5555-6666" } ] }
		          ]
		        }
		      ]
		    }
		  ]
		}
		""";

	public const string DeprecatedReport = """
		{
		  "version": 1,
		  "parameters": "--deprecated",
		  "sources": [ "https://api.nuget.org/v3/index.json" ],
		  "projects": [
		    {
		      "path": "C:/repo/src/P/P.csproj",
		      "frameworks": [
		        {
		          "framework": "net10.0",
		          "topLevelPackages": [
		            { "id": "Contoso.Old", "requestedVersion": "3.0.0", "resolvedVersion": "3.0.0",
		              "deprecationReasons": [ "Legacy", "CriticalBugs" ],
		              "alternativePackage": { "id": "Contoso.New", "versionRange": ">= 4.0.0" } }
		          ]
		        }
		      ]
		    }
		  ]
		}
		""";

	public const string Solution = """
		<Solution>
		  <Folder Name="/Solution Items/">
		    <File Path="README.md"/>
		  </Folder>
		  <Project Path="libs/A/A.csproj"/>
		  <Folder Name="/tests/">
		    <Project Path="tests\B.Tests\B.Tests.csproj">
		      <Platform Project="x64"/>
		    </Project>
		  </Folder>
		</Solution>
		""";

	public const string Markdown = """
		# Links

		See [the NuGet docs](https://learn.microsoft.com/nuget/concepts/auditing-packages#running-nuget-audit-in-ci), again
		https://learn.microsoft.com/nuget/concepts/auditing-packages#running-nuget-audit-in-ci, and <https://www.contributor-covenant.org/faq>.
		A sentence ends with https://docs.zizmor.sh/usage/.
		Internal: https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/SECURITY.md and
		https://github.com/CheatEngineNet/CheatEngine.SDK/tree/main/docs are checked offline, but
		https://github.com/CheatEngineNet/CheatEngine.SDK/security/advisories/new is requested.
		Skipped: http://localhost:5000/x, https://www.example.com/a, https://api.nuget.org/v3-flatcontainer/cheatengine.sdk/{version}/x.
		Templates: `https://raw.githubusercontent.com/CheatEngineNet/CheatEngine.SDK/<same commit>/` and
		https://api.nuget.org/v3-flatcontainer/<id>/index.json are not links, nor is ``code with `https://in-code.invalid` ``.

		```powershell
		Invoke-WebRequest https://in-a-fence.invalid/never
		```

		| Table | https://github.com/cheat-engine/cheat-engine |
		""";

	public static readonly string NeedsAllSucceeded = """
		{ "audit": { "result": "success", "outputs": {} }, "canary": { "result": "success", "outputs": {} },
		  "links": { "result": "success", "outputs": { "broken": "false" } } }
		""";

	public static readonly string NeedsOneFailed = """
		{ "audit": { "result": "failure", "outputs": {} }, "canary": { "result": "success", "outputs": {} },
		  "test-repeat": { "result": "cancelled", "outputs": {} } }
		""";

	public static readonly string NeedsInjected = """
		{ "evil|job\n| x": { "result": "success" }, "canary": { "result": "<script>" } }
		""";

	public static readonly string IssueList = """
		[ { "number": 12, "title": "Scheduled health check needs attention (old)" },
		  { "number": 9, "title": "scheduled health check needs attention" },
		  { "number": 15, "title": "Scheduled health check needs attention" },
		  { "number": 11, "title": "Scheduled health check needs attention" } ]
		""";

	public static readonly PwshCall[] All =
	[
		Call("tag_newest_by_version", "Select-ReleaseTag", ("Tag", new[] { "v0.1.0", "v1.0.0", "v0.2.1", "v1.10.0", "v1.9.3" })),
		Call("tag_ignores_prerelease_and_malformed", "Select-ReleaseTag",
			("Tag", new[] { "v2.0.0-alpha.1", "v1.0.0", "1.2.0", "v1.0", "v01.2.3", "v1.0.0.1", "release-3.0.0", "V3.0.0" })),
		Call("tag_none", "Select-ReleaseTag", ("Tag", new[] { "latest", "v2.0.0-rc.1" })),
		Call("tag_empty", "Select-ReleaseTag", ("Tag", Array.Empty<string>())),

		Drift("drift_reproduced", HashA, HashA, Fingerprint, HashA, HashA),
		Drift("drift_toolchain", HashB, HashB, Fingerprint, HashA, HashA),
		Drift("drift_path_dependent", HashB, HashC, Fingerprint, HashA, HashA),
		Drift("drift_fingerprint_mismatch", HashA, HashA, "0000:1111", HashA, HashA),
		Drift("drift_fingerprint_missing", HashA, HashA, null, HashA, HashA),
		Drift("drift_release_unreadable", HashA, HashA, Fingerprint, HashA, null),
		Drift("drift_committed_differs_from_release", HashB, HashB, Fingerprint, HashC, HashA),
		Drift("drift_uppercase_hash_is_malformed", HashA.ToUpperInvariant(), HashA.ToUpperInvariant(), Fingerprint, HashA, HashA),

		Call("channel_of_feature_band", "Get-SdkChannel", ("Version", "10.0.401")),
		Call("channel_of_next_major", "Get-SdkChannel", ("Version", "11.0.100")),
		Call("channel_rejects_short_version", "Get-SdkChannel", ("Version", "10.0.4")),
		Call("channel_rejects_prerelease", "Get-SdkChannel", ("Version", "10.0.100-preview.7.25380.108")),
		Call("newest_sdk", "Get-NewestSdkVersion", ("ReleasesJson", """{ "channel-version": "10.0", "latest-sdk": "10.0.402" }"""), ("Channel", "10.0")),
		Call("newest_sdk_other_channel", "Get-NewestSdkVersion", ("ReleasesJson", """{ "latest-sdk": "11.0.100" }"""), ("Channel", "10.0")),
		Call("newest_sdk_missing", "Get-NewestSdkVersion", ("ReleasesJson", """{ "latest-release": "10.0.12" }"""), ("Channel", "10.0")),
		Call("global_json_rejects_invalid_version", "Get-UpdatedGlobalJson", ("Json", """{ "sdk": { "version": "10.0.401" } }"""),
			("Version", "latest")),
		Call("global_json_without_sdk_version", "Get-UpdatedGlobalJson", ("Json", """{ "test": { "runner": "Microsoft.Testing.Platform" } }"""),
			("Version", "10.0.402")),
		// errorMessage follows the version, as whole versions only; a message that names no version stays as it is.
		Call("global_json_error_message_follows_the_version", "Get-UpdatedGlobalJson",
			("Json", """{ "sdk": { "version": "10.0.401", "errorMessage": "Needs 10.0.401 (v10.0.401), not 10.0.4012, 110.0.401 or 10.0.401.1. Run: install --version 10.0.401." } }"""),
			("Version", "10.0.402")),
		Call("global_json_error_message_without_a_version", "Get-UpdatedGlobalJson",
			("Json", """{ "sdk": { "version": "10.0.401", "errorMessage": "Install the SDK global.json names." } }"""),
			("Version", "10.0.402")),
		Call("global_json_without_error_message", "Get-UpdatedGlobalJson",
			("Json", """{ "sdk": { "version": "10.0.401", "rollForward": "disable" } }"""), ("Version", "10.0.402")),

		Call("package_reference_present", "Test-PackageReference",
			("ProjectText", """<ItemGroup><PackageReference Include="Microsoft.Testing.Extensions.HangDump"/></ItemGroup>"""),
			("PackageId", "Microsoft.Testing.Extensions.HangDump")),
		Call("package_reference_absent", "Test-PackageReference",
			("ProjectText", """<ItemGroup><PackageReference Include="Microsoft.Testing.Extensions.TrxReport"/></ItemGroup>"""),
			("PackageId", "Microsoft.Testing.Extensions.HangDump")),
		Call("package_reference_prefix_is_not_a_match", "Test-PackageReference",
			("ProjectText", """<PackageReference Include="Microsoft.Testing.Extensions.HangDumpPlus"/>"""),
			("PackageId", "Microsoft.Testing.Extensions.HangDump")),

		Call("trx_counters", "Get-TrxSummary", ("Xml", Trx)),
		Call("trx_without_summary", "Get-TrxSummary", ("Xml", "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"/>")),
		Call("package_list_vulnerable", "ConvertFrom-PackageListReport", ("Json", VulnerableReport), ("Kind", "Vulnerable"),
			("RepositoryRoot", "C:/repo")),
		Call("package_list_deprecated", "ConvertFrom-PackageListReport", ("Json", DeprecatedReport), ("Kind", "Deprecated"),
			("RepositoryRoot", "C:/repo")),
		Call("package_list_clean", "ConvertFrom-PackageListReport",
			("Json", """{ "version": 1, "projects": [ { "path": "C:/repo/libs/A/A.csproj" } ] }"""), ("Kind", "Vulnerable")),
		Call("package_list_unknown_version", "ConvertFrom-PackageListReport", ("Json", """{ "version": 2, "projects": [] }"""),
			("Kind", "Vulnerable")),
		Call("solution_projects", "Get-SolutionProjectPath", ("SolutionXml", Solution)),

		Call("links_extracted", "Get-ExternalLinkTarget", ("Markdown", Markdown), ("RepositorySlug", "CheatEngineNet/CheatEngine.SDK")),
		Verdict("verdict_200", 200), Verdict("verdict_301", 301), Verdict("verdict_404", 404), Verdict("verdict_410", 410),
		Verdict("verdict_429", 429), Verdict("verdict_403", 403), Verdict("verdict_500", 500), Verdict("verdict_no_answer", 0),

		Call("issue_healthy", "Get-HealthIssueReport", ("NeedsJson", NeedsAllSucceeded), ("Drift", "false"), ("BrokenLinks", "false"),
			("RunUrl", RunUrl)),
		Call("issue_failed_job", "Get-HealthIssueReport", ("NeedsJson", NeedsOneFailed), ("RunUrl", RunUrl)),
		Call("issue_drift_only", "Get-HealthIssueReport", ("NeedsJson", NeedsAllSucceeded), ("Drift", "true"), ("RunUrl", RunUrl)),
		Call("issue_broken_links_only", "Get-HealthIssueReport", ("NeedsJson", NeedsAllSucceeded), ("BrokenLinks", "true")),
		Call("issue_sanitizes_values", "Get-HealthIssueReport", ("NeedsJson", NeedsInjected),
			("RunUrl", "https://evil.example/actions/runs/1 [click](https://evil.example)")),
		Call("issue_rejects_empty_needs", "Get-HealthIssueReport", ("NeedsJson", "{}")),
		Call("issue_selected_exact_title_lowest_number", "Select-HealthIssue", ("IssueListJson", IssueList)),
		Call("issue_none_open", "Select-HealthIssue", ("IssueListJson", """[ { "number": 3, "title": "Other" } ]""")),
		Call("issue_list_empty", "Select-HealthIssue", ("IssueListJson", "[]"))
	];

	private static PwshCall Call(string name, string function, params (string Name, object? Value)[] arguments)
	{
		Dictionary<string, object?> values = new(StringComparer.Ordinal);
		foreach ((string argument, object? value) in arguments)
		{
			values[argument] = value;
		}

		return new PwshCall(name, function, values);
	}

	private static PwshCall Drift(string name, string rebuilt, string second, string? exported, string committed, string? released)
	{
		return Call(name, "Get-BridgeDriftClassification", ("RebuiltSha256", rebuilt), ("SecondRebuiltSha256", second),
			("ExpectedFingerprint", Fingerprint), ("ExportedFingerprint", exported), ("CheckedInSha256", committed),
			("ReleasedSha256", released));
	}

	private static PwshCall Verdict(string name, int statusCode)
	{
		return Call(name, "Get-ExternalLinkVerdict", ("StatusCode", statusCode));
	}
}
