namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>Evaluates every <see cref="HealthCheckCases" /> vector in one <c>pwsh</c> process.</summary>
public sealed class HealthCheckFixture : IAsyncLifetime
{
	/// <summary>The case that rewrites the repository's own global.json.</summary>
	public const string GlobalJsonCase = "global_json_rewritten";

	public const string CanaryVersion = "10.0.499";

	private Dictionary<string, PwshCallResult> _results = new(StringComparer.Ordinal);

	/// <summary>The functions the module exports.</summary>
	internal IReadOnlyList<string> ExportedFunctions
	{
		get
		{
			List<string> names = [];
			foreach (System.Text.Json.JsonElement name in Result(PwshFunctionBatch.ExportedFunctions).Items("exports"))
			{
				names.Add(name.GetString() ?? "");
			}

			return names;
		}
	}

	/// <summary>The result of a case.</summary>
	internal PwshCallResult Result(string caseName)
	{
		Assert.True(_results.TryGetValue(caseName, out PwshCallResult? result), $"The health-check module returned no result for '{caseName}'.");
		return result;
	}

	public async ValueTask InitializeAsync()
	{
		List<PwshCall> calls = [.. HealthCheckCases.All];
		calls.Add(new PwshCall(GlobalJsonCase, "Get-UpdatedGlobalJson", new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Json"] = RepositoryFile.ReadText("global.json"),
			["Version"] = CanaryVersion
		}));
		_results = await PwshFunctionBatch.RunAsync(HealthCheckCases.ModulePath, calls);
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
