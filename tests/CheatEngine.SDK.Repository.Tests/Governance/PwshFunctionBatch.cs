using System.Text;
using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     Evaluates many calls of one PowerShell module in a single <c>pwsh</c> process (see <see cref="PwshScript" />).
///     Arguments and results travel through UTF-8 JSON files, so no test value is ever interpolated into script text.
/// </summary>
internal static class PwshFunctionBatch
{
	/// <summary>The key of the extra result that lists the functions the module exports.</summary>
	public const string ExportedFunctions = "__exported";

	public static async Task<Dictionary<string, PwshCallResult>> RunAsync(string modulePath, IReadOnlyList<PwshCall> calls)
	{
		using TemporaryDirectory directory = new();
		string callsPath = directory.File("calls.json");
		string resultsPath = directory.File("results.json");

		List<Dictionary<string, object?>> payload = [];
		foreach (PwshCall call in calls)
		{
			payload.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["name"] = call.Name,
				["function"] = call.Function,
				["arguments"] = call.Arguments
			});
		}

		await File.WriteAllTextAsync(callsPath, JsonSerializer.Serialize(payload), new UTF8Encoding(false),
			TestContext.Current.CancellationToken);

		string script = $$"""
			$module = Import-Module -Name {{PwshScript.Quote(RepositoryFile.FullPath(modulePath))}} -Force -PassThru
			$calls = Get-Content -Raw -Encoding utf8 -LiteralPath {{PwshScript.Quote(callsPath)}} | ConvertFrom-Json -AsHashtable
			$results = [ordered]@{}
			foreach ($call in $calls) {
			    $arguments = $call['arguments']
			    try {
			        $value = @(& $call['function'] @arguments)
			        $results[$call['name']] = [ordered]@{ output = $value; error = $null }
			    }
			    catch {
			        $results[$call['name']] = [ordered]@{ output = @(); error = $_.Exception.Message }
			    }
			}
			$results['{{ExportedFunctions}}'] = [ordered]@{ output = @($module.ExportedFunctions.Keys | Sort-Object); error = $null }
			$json = ConvertTo-Json -InputObject $results -Depth 12
			[System.IO.File]::WriteAllText({{PwshScript.Quote(resultsPath)}}, $json, [System.Text.UTF8Encoding]::new($false))
			""";
		PwshResult run = await PwshScript.RunTextAsync(script);
		Assert.True(run.ExitCode == 0, $"Evaluating {modulePath} failed: {run.Transcript}");

		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(resultsPath, TestContext.Current.CancellationToken));
		Dictionary<string, PwshCallResult> results = new(StringComparer.Ordinal);
		foreach (JsonProperty entry in document.RootElement.EnumerateObject())
		{
			JsonElement output = entry.Value.GetProperty("output");
			JsonElement error = entry.Value.GetProperty("error");
			results[entry.Name] = new PwshCallResult(
				output.ValueKind == JsonValueKind.Array ? output.Clone() : JsonSerializer.SerializeToElement(new[] { output.Clone() }),
				error.ValueKind == JsonValueKind.Null ? null : error.GetString());
		}

		return results;
	}
}
