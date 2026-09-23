using System.Text;
using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     Evaluates every <see cref="PullRequestPolicyCases" /> vector in a single <c>pwsh</c> process that imports
///     <c>eng/ci/PullRequestPolicy.psm1</c>. Inputs and outputs travel through UTF-8 JSON files, so no title or path is
///     ever interpolated into script text.
/// </summary>
public sealed class PullRequestPolicyFixture : IAsyncLifetime
{
	private readonly Dictionary<string, List<PolicyRuleResult>> _results = new(StringComparer.Ordinal);

	/// <summary>The rule results of a case, in reported order.</summary>
	internal IReadOnlyList<PolicyRuleResult> ResultsOf(string caseName)
	{
		Assert.True(_results.TryGetValue(caseName, out List<PolicyRuleResult>? results),
			$"The policy module returned no result for '{caseName}'.");
		return results;
	}

	public async ValueTask InitializeAsync()
	{
		using TemporaryDirectory directory = new();
		string casesPath = directory.File("cases.json");
		string resultsPath = directory.File("results.json");

		List<Dictionary<string, object>> cases = [];
		foreach (PullRequestPolicyCase policyCase in PullRequestPolicyCases.All)
		{
			cases.Add(new Dictionary<string, object>(StringComparer.Ordinal)
			{
				["name"] = policyCase.Name,
				["title"] = policyCase.Title,
				["body"] = policyCase.Body,
				["author"] = policyCase.Author,
				["changedFiles"] = policyCase.ChangedFiles
			});
		}

		await File.WriteAllTextAsync(casesPath, JsonSerializer.Serialize(cases), new UTF8Encoding(false),
			TestContext.Current.CancellationToken);

		string script = $$"""
			Import-Module -Name {{PwshScript.Quote(RepositoryFile.FullPath("eng/ci/PullRequestPolicy.psm1"))}} -Force
			$cases = Get-Content -Raw -Encoding utf8 -LiteralPath {{PwshScript.Quote(casesPath)}} | ConvertFrom-Json
			$output = [ordered]@{}
			foreach ($case in $cases) {
			    $results = @(Test-PullRequestPolicy -Title $case.title -Body $case.body -Author $case.author -ChangedFile @($case.changedFiles))
			    $output[$case.name] = @($results | ForEach-Object { [ordered]@{ rule = $_.Rule; passed = $_.Passed; message = $_.Message } })
			}
			$json = ConvertTo-Json -InputObject $output -Depth 5
			[System.IO.File]::WriteAllText({{PwshScript.Quote(resultsPath)}}, $json, [System.Text.UTF8Encoding]::new($false))
			""";
		PwshResult run = await PwshScript.RunTextAsync(script);
		Assert.True(run.ExitCode == 0, $"Evaluating the PR policy vectors failed: {run.Transcript}");

		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(resultsPath,
			TestContext.Current.CancellationToken));
		foreach (JsonProperty entry in document.RootElement.EnumerateObject())
		{
			List<PolicyRuleResult> results = [];
			foreach (JsonElement result in entry.Value.EnumerateArray())
			{
				results.Add(new PolicyRuleResult(
					result.GetProperty("rule").GetString() ?? "",
					result.GetProperty("passed").GetBoolean(),
					result.GetProperty("message").GetString() ?? ""));
			}

			_results[entry.Name] = results;
		}
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
