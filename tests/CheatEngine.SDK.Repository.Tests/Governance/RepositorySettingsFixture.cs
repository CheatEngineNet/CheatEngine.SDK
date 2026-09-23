namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>Evaluates the <c>eng/github/RepositorySettings.psm1</c> vectors in one <c>pwsh</c> process.</summary>
public sealed class RepositorySettingsFixture : IAsyncLifetime
{
	public const string ModulePath = "eng/github/RepositorySettings.psm1";

	private Dictionary<string, PwshCallResult> _results = new(StringComparer.Ordinal);

	/// <summary>The result of a vector.</summary>
	internal PwshCallResult Result(string name)
	{
		Assert.True(_results.TryGetValue(name, out PwshCallResult? result), $"The settings module returned no result for '{name}'.");
		return result;
	}

	public async ValueTask InitializeAsync()
	{
		_results = await PwshFunctionBatch.RunAsync(ModulePath, RepositorySettingsCases.Calls());
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
