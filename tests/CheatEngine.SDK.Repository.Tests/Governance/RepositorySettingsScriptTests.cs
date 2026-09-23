using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     <c>eng/github/Set-RepositorySettings.ps1</c> and its module (audit register PR-CQ-17): the comparison rules, the
///     plan, and end-to-end runs against a recording <c>gh</c> that serves GitHub responses: the plan never calls
///     GitHub, CI is refused, a run writes only what differs, a second run writes nothing, <c>-WhatIf</c> never writes,
///     and <c>-SkipRequiredChecks</c> never removes required checks already in place. Nothing here reaches GitHub.
/// </summary>
public sealed class RepositorySettingsScriptTests(RepositorySettingsFixture fixture) : IClassFixture<RepositorySettingsFixture>
{
	private const string Script = "eng/github/Set-RepositorySettings.ps1";
	private const string Base = RepositorySettingsCases.Base;

	private static readonly string[] s_planOrder =
	[
		"repository", "vulnerability-alerts", "automated-security-fixes", "private-vulnerability-reporting", "label:compatibility",
		"label:bug", "label:ci", "label:dependencies", "label:.NET", "ruleset:Protect main", "ruleset:Protect release tags",
		"environment:nuget", "actions-permissions", "actions-workflow-permissions"
	];

	[Theory]
	[InlineData("same", new string[0])]
	[InlineData("scalar_differs", new[] { "$.t: desired \"PR_TITLE\", live \"COMMIT_OR_PR_TITLE\"" })]
	[InlineData("false_is_not_zero", new[] { "$.a: desired false, live 0" })]
	[InlineData("sets_ignore_order", new string[0])]
	[InlineData("sets_differ", new[] { "$.m: desired [\"squash\"], live [\"merge\", \"rebase\", \"squash\"]" })]
	[InlineData("rule_missing", new[] { "$.rules[required_status_checks]: missing" })]
	[InlineData("rule_extra", new[] { "$.rules[non_fast_forward]: present live but not in the payload (a PUT removes it)" })]
	[InlineData("nested_rule_parameter",
		new[] { "$.rules[pull_request].parameters.allowed_merge_methods: desired [\"squash\"], live [\"merge\", \"rebase\", \"squash\"]" })]
	[InlineData("status_checks_by_context", new[] { "$.c[PR policy]: missing" })]
	[InlineData("reviewers_by_type_and_id",
		new[] { "$.reviewers[User#1]: missing", "$.reviewers[User#2]: present live but not in the payload (a PUT removes it)" })]
	[InlineData("bypass_by_actor", new[] { "$.b[RepositoryRole#5].bypass_mode: desired \"always\", live \"pull_request\"" })]
	[InlineData("object_absent", new[] { "$.a: expected an object, found (absent)" })]
	public void Settings_comparison_reports_only_what_the_payload_manages(string caseName, string[] differences)
	{
		List<string> actual = [];
		foreach (JsonElement line in fixture.Result(caseName).Items(caseName))
		{
			actual.Add(line.GetString() ?? "");
		}

		Assert.Equal(differences, actual, StringComparer.Ordinal);
	}

	[Fact]
	public void Unmanaged_live_fields_are_listed_without_read_only_metadata()
	{
		List<string> fields = [];
		foreach (JsonElement line in fixture.Result("unmanaged_nested").Items("unmanaged"))
		{
			fields.Add(line.GetString() ?? "");
		}

		Assert.Equal(["$.enforcement = \"active\"", "$.rules[pull_request].parameters.require_extra_approval_for_unattributed_changes = true"],
			fields.Order(StringComparer.Ordinal), StringComparer.Ordinal);
	}

	[Fact]
	public void Environment_response_is_read_in_the_shape_of_the_put_body()
	{
		JsonElement live = fixture.Result("environment_without_reviewers").Single("live");
		Assert.Equal(0, live.GetProperty("wait_timer").GetInt32());
		Assert.False(live.GetProperty("prevent_self_review").GetBoolean());
		Assert.True(live.GetProperty("can_admins_bypass").GetBoolean());
		Assert.Equal(0, live.GetProperty("reviewers").GetArrayLength());
		Assert.True(live.GetProperty("deployment_branch_policy").GetProperty("custom_branch_policies").GetBoolean());

		JsonElement reviewed = fixture.Result("environment_with_reviewers").Single("reviewed");
		Assert.Equal(5, reviewed.GetProperty("wait_timer").GetInt32());
		Assert.True(reviewed.GetProperty("prevent_self_review").GetBoolean());
		Assert.False(reviewed.GetProperty("can_admins_bypass").GetBoolean());
		JsonElement reviewer = Assert.Single(reviewed.GetProperty("reviewers").EnumerateArray());
		Assert.Equal("User", reviewer.GetProperty("type").GetString());
		Assert.Equal(RepositorySettingsCases.ReviewerId, reviewer.GetProperty("id").GetInt64());
	}

	[Fact]
	public void Settings_plan_follows_the_documented_order()
	{
		Assert.Equal(s_planOrder, PlanIds("plan_default"), StringComparer.Ordinal);
		Assert.Equal([.. s_planOrder, "immutable-releases"], PlanIds("plan_immutable_releases"));

		// -SkipRequiredChecks: the main ruleset keeps whatever checks are live instead of requiring them.
		JsonElement main = Step("plan_skip_required_checks", "ruleset:Protect main");
		Assert.True(main.GetProperty("KeepLiveRequiredChecks").GetBoolean());
		Assert.DoesNotContain("required_status_checks", main.GetProperty("Desired").GetRawText(), StringComparison.Ordinal);
		Assert.Contains("required_status_checks", Step("plan_default", "ruleset:Protect main").GetProperty("Desired").GetRawText(),
			StringComparison.Ordinal);
		// Payload comments are never sent.
		Assert.DoesNotContain("_comment", fixture.Result("plan_default").Output.GetRawText(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Plan_only_prints_every_step_without_calling_github()
	{
		(PwshResult run, List<string> calls) = await RunAsync(RepositorySettingsCases.LiveResponses(), "-PlanOnly");

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Empty(calls);
		int previous = -1;
		for (int i = 0; i < s_planOrder.Length; i++)
		{
			int position = run.StandardOutput.IndexOf($"[{i + 1}] {s_planOrder[i]}: ", StringComparison.Ordinal);
			Assert.True(position > previous, $"Step {i + 1} ({s_planOrder[i]}) is missing or out of order:{Environment.NewLine}{run.StandardOutput}");
			previous = position;
		}

		Assert.Contains("\"context\": \"PR policy\"", run.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("reviewers: users AriusII, resolved to ids at run time", run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Settings_script_refuses_to_run_in_ci()
	{
		(PwshResult run, List<string> calls) = await RunAsync(RepositorySettingsCases.LiveResponses(), ["-PlanOnly"],
			new Dictionary<string, string>(StringComparer.Ordinal) { ["CI"] = "true" });

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("refuses to run in CI", run.StandardError + run.StandardOutput, StringComparison.Ordinal);
		Assert.Empty(calls);
	}

	[Fact]
	public async Task Settings_script_writes_only_what_differs_from_the_live_settings()
	{
		(PwshResult run, List<string> calls) = await RunAsync(RepositorySettingsCases.LiveResponses(), "-Confirm:$false");

		Assert.True(run.ExitCode == 0, run.Transcript);
		List<string> writes = Writes(calls);
		Assert.Equal(
			[
				$"PATCH {Base}",
				$"POST {Base}/labels",
				$"PUT {Base}/rulesets/{RepositorySettingsCases.MainRulesetId}",
				$"POST {Base}/rulesets",
				$"PUT {Base}/environments/nuget",
				$"PUT {Base}/actions/permissions"
			],
			writes.ConvertAll(static write => string.Join(' ', write.Split(' ', 3)[..2])));

		string main = Body(writes, $"PUT {Base}/rulesets/{RepositorySettingsCases.MainRulesetId}");
		Assert.Contains("\"allowed_merge_methods\":[\"squash\"]", main, StringComparison.Ordinal);
		Assert.Contains("{\"context\":\"CI / Gate\",\"integration_id\":15368}", main, StringComparison.Ordinal);
		Assert.Contains("{\"context\":\"PR policy\",\"integration_id\":15368}", main, StringComparison.Ordinal);
		string environment = Body(writes, $"PUT {Base}/environments/nuget");
		Assert.Contains($"\"reviewers\":[{{\"type\":\"User\",\"id\":{RepositorySettingsCases.ReviewerId}}}]", environment, StringComparison.Ordinal);
		Assert.Contains("\"can_admins_bypass\":false", environment, StringComparison.Ordinal);
		Assert.Contains("\"name\":\"compatibility\"", Body(writes, $"POST {Base}/labels"), StringComparison.Ordinal);
		Assert.Contains("\"sha_pinning_required\":true", Body(writes, $"PUT {Base}/actions/permissions"), StringComparison.Ordinal);
		// Nothing is ever sent for the payload comments, and the code scanning default setup is only read.
		Assert.All(writes, static write => Assert.DoesNotContain("_comment", write, StringComparison.Ordinal));
		Assert.Contains($"GET {Base}/code-scanning/default-setup", calls, StringComparer.Ordinal);
	}

	[Fact]
	public async Task Settings_script_changes_nothing_once_the_settings_are_applied()
	{
		(PwshResult run, List<string> calls) = await RunAsync(RepositorySettingsCases.AppliedResponses(), "-Confirm:$false");

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Empty(Writes(calls));
		Assert.Contains($"Every setting of {RepositorySettingsCases.Repository} matches eng/github.", run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task What_if_reads_the_live_settings_and_never_writes()
	{
		(PwshResult run, List<string> calls) = await RunAsync(RepositorySettingsCases.LiveResponses(), "-WhatIf");

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Empty(Writes(calls));
		Assert.Contains("differs: repository.allow_merge_commit: desired false, live true", run.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("differs: ruleset:Protect release tags: does not exist", run.StandardOutput, StringComparison.Ordinal);
		Assert.Contains(
			"unmanaged (left as is): ruleset:Protect main.rules[pull_request].parameters.require_extra_approval_for_unattributed_changes = true",
			run.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("WhatIf: nothing was written.", run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Skip_required_checks_never_removes_the_checks_already_required()
	{
		(PwshResult applied, List<string> appliedCalls) = await RunAsync(RepositorySettingsCases.AppliedResponses(), "-SkipRequiredChecks",
			"-Confirm:$false");
		Assert.True(applied.ExitCode == 0, applied.Transcript);
		Assert.Empty(Writes(appliedCalls));

		(PwshResult first, List<string> firstCalls) = await RunAsync(RepositorySettingsCases.LiveResponses(), "-SkipRequiredChecks",
			"-Confirm:$false");
		Assert.True(first.ExitCode == 0, first.Transcript);
		string main = Body(Writes(firstCalls), $"PUT {Base}/rulesets/{RepositorySettingsCases.MainRulesetId}");
		Assert.DoesNotContain("required_status_checks", main, StringComparison.Ordinal);
		Assert.Contains("\"allowed_merge_methods\":[\"squash\"]", main, StringComparison.Ordinal);
	}

	private List<string> PlanIds(string caseName)
	{
		List<string> ids = [];
		foreach (JsonElement step in fixture.Result(caseName).Items(caseName))
		{
			ids.Add(step.GetProperty("Id").GetString() ?? "");
		}

		return ids;
	}

	private JsonElement Step(string caseName, string id)
	{
		return Assert.Single(fixture.Result(caseName).Items(caseName),
			step => string.Equals(step.GetProperty("Id").GetString(), id, StringComparison.Ordinal));
	}

	private static List<string> Writes(List<string> calls)
	{
		return calls.FindAll(static call => !call.StartsWith("GET ", StringComparison.Ordinal) && !call.StartsWith("AUTH", StringComparison.Ordinal));
	}

	private static string Body(List<string> writes, string request)
	{
		string write = Assert.Single(writes, write => write.StartsWith(request + " ", StringComparison.Ordinal));
		return write[(request.Length + 1)..];
	}

	private static Task<(PwshResult Run, List<string> Calls)> RunAsync(Dictionary<string, string> responses, params string[] arguments)
	{
		return RunAsync(responses, arguments, null);
	}

	/// <summary>
	///     A recorder standing in for <c>gh</c>: <c>gh api --include</c> answers from the response files (endpoint → JSON; an
	///     empty body is a 204, a missing endpoint a 404), writes answer <c>{}</c>, and every call is logged as
	///     <c>METHOD endpoint [body]</c>.
	/// </summary>
	private const string FakeGh = """
		if ($args[0] -ceq 'auth') {
		    Add-Content -LiteralPath $env:FAKE_GH_LOG -Value 'AUTH' -Encoding utf8
		    exit 0
		}
		$method = 'GET'
		$endpoint = $null
		$inputFile = $null
		for ($i = 1; $i -lt $args.Count; $i++) {
		    $argument = [string] $args[$i]
		    if ($argument -ceq '--method') { $method = [string] $args[$i + 1]; $i++ }
		    elseif ($argument -ceq '-H') { $i++ }
		    elseif ($argument -ceq '--input') { $inputFile = [string] $args[$i + 1]; $i++ }
		    elseif (-not $argument.StartsWith('-')) { $endpoint = $argument }
		}
		$line = "$method $endpoint"
		if ($inputFile) {
		    $line += ' ' + (Get-Content -Raw -LiteralPath $inputFile | ConvertFrom-Json | ConvertTo-Json -Depth 20 -Compress)
		}
		Add-Content -LiteralPath $env:FAKE_GH_LOG -Value $line -Encoding utf8
		if ($method -cne 'GET') {
		    'HTTP/2.0 200 OK'; 'Content-Type: application/json'; ''; '{}'
		    exit 0
		}
		$file = Join-Path $env:FAKE_GH_RESPONSES (($endpoint -replace '[^A-Za-z0-9._-]', '_') + '.json')
		if (-not (Test-Path -LiteralPath $file)) {
		    'HTTP/2.0 404 Not Found'; 'Content-Type: application/json'; ''; '{"message":"Not Found"}'
		    exit 1
		}
		$body = Get-Content -Raw -LiteralPath $file
		if ([string]::IsNullOrWhiteSpace($body)) {
		    'HTTP/2.0 204 No Content'; ''
		    exit 0
		}
		'HTTP/2.0 200 OK'; 'Content-Type: application/json'; ''; $body
		exit 0
		""";

	/// <summary>Runs the settings script with <c>gh</c> replaced by <see cref="FakeGh" /> serving <paramref name="responses" />.</summary>
	private static async Task<(PwshResult Run, List<string> Calls)> RunAsync(Dictionary<string, string> responses, string[] arguments,
		Dictionary<string, string>? extraEnvironment)
	{
		using TemporaryDirectory directory = new();
		string bin = directory.File("bin");
		string responseDirectory = directory.File("responses");
		string log = directory.File("gh.log");
		Directory.CreateDirectory(bin);
		Directory.CreateDirectory(responseDirectory);
		foreach ((string endpoint, string body) in responses)
		{
			await File.WriteAllTextAsync(Path.Combine(responseDirectory, FileNameOf(endpoint)), body, TestContext.Current.CancellationToken);
		}

		await File.WriteAllTextAsync(log, "", TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(bin, "gh.ps1"), FakeGh, TestContext.Current.CancellationToken);

		Dictionary<string, string> environment = new(extraEnvironment ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal)
		{
			["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
			["FAKE_GH_LOG"] = log,
			["FAKE_GH_RESPONSES"] = responseDirectory
		};

		PwshResult run = await PwshScript.RunFileAsync(Script, arguments, environment);
		List<string> calls = [];
		foreach (string line in (await File.ReadAllTextAsync(log, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n").Split('\n'))
		{
			if (line.Length > 0)
			{
				calls.Add(line);
			}
		}

		return (run, calls);
	}

	private static string FileNameOf(string endpoint)
	{
		char[] name = endpoint.ToCharArray();
		for (int i = 0; i < name.Length; i++)
		{
			if (!char.IsAsciiLetterOrDigit(name[i]) && name[i] is not ('.' or '_' or '-'))
			{
				name[i] = '_';
			}
		}

		return new string(name) + ".json";
	}
}
