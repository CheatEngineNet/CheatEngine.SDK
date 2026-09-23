using System.Text;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <c>Get-ReleaseAssetPlan</c> decides what the <c>draft-release</c> job of <c>release.yml</c> may do. Immutable releases
///     lock assets once published, so the plan never uploads to a published release, never replaces a draft asset other
///     than the tuple, and asks for a human when a draft or published asset differs from what this run produced.
/// </summary>
public sealed class ReleaseAssetPlanTests
{
	private const string Tuple = "CheatEngine.SDK.2.0.0.tuple.json";
	private const string Package = "CheatEngine.SDK.2.0.0.nupkg";
	private const string Sums = "SHA256SUMS";

	private static readonly string s_packageHash = new('a', 64);
	private static readonly string s_sumsHash = new('b', 64);
	private static readonly string s_tupleHash = new('c', 64);

	[Fact]
	public async Task A_tag_without_a_release_gets_a_draft_with_every_asset()
	{
		JsonElement plan = await PlanAsync(null);

		Assert.Equal("Create", plan.GetProperty("Action").GetString());
		Assert.Equal([Package, Tuple, Sums], Strings(plan, "Upload"));
	}

	[Fact]
	public async Task A_draft_receives_its_missing_assets_and_only_the_tuple_is_replaced()
	{
		JsonElement plan = await PlanAsync(Release(isDraft: true, (Package, s_packageHash), (Tuple, new string('d', 64))));

		Assert.Equal("Complete", plan.GetProperty("Action").GetString());
		Assert.Equal([Sums], Strings(plan, "Upload"));
		Assert.Equal([Tuple], Strings(plan, "Replace"));
	}

	[Fact]
	public async Task A_draft_with_another_package_needs_a_maintainer()
	{
		JsonElement plan = await PlanAsync(Release(isDraft: true, (Package, new string('e', 64)), (Sums, s_sumsHash)));

		Assert.Equal("Refuse", plan.GetProperty("Action").GetString());
		Assert.Contains(Package, Assert.Single(Strings(plan, "Problems")), StringComparison.Ordinal);
		Assert.Empty(Strings(plan, "Upload"));
	}

	[Fact]
	public async Task A_published_release_with_the_same_assets_is_left_untouched()
	{
		JsonElement plan = await PlanAsync(Release(isDraft: false, (Package, s_packageHash), (Sums, s_sumsHash),
			(Tuple, new string('f', 64)), ("CheatEngine.SDK.2.0.0.tuple.sigstore.json", new string('1', 64))));

		Assert.Equal("AlreadyPublished", plan.GetProperty("Action").GetString());
		Assert.Empty(Strings(plan, "Upload"));
		Assert.Empty(Strings(plan, "Replace"));
	}

	[Fact]
	public async Task A_published_release_is_never_uploaded_to()
	{
		JsonElement missing = await PlanAsync(Release(isDraft: false, (Package, s_packageHash)));
		JsonElement different = await PlanAsync(Release(isDraft: false, (Package, s_packageHash), (Sums, new string('2', 64)),
			(Tuple, s_tupleHash)));

		Assert.Equal("Refuse", missing.GetProperty("Action").GetString());
		Assert.Contains("the published release has no 'SHA256SUMS'", Strings(missing, "Problems"), StringComparer.Ordinal);
		Assert.Empty(Strings(missing, "Upload"));
		Assert.Equal("Refuse", different.GetProperty("Action").GetString());
		Assert.Empty(Strings(different, "Upload"));
		Assert.Empty(Strings(different, "Replace"));
	}

	private static async Task<JsonElement> PlanAsync(string? releaseJson)
	{
		string release = releaseJson is null ? "$null" : $"({PowerShellScript.Literal(releaseJson)} | ConvertFrom-Json)";
		string local = $"([ordered]@{{ {PowerShellScript.Literal(Tuple)} = '{s_tupleHash}'; " +
					   $"{PowerShellScript.Literal(Package)} = '{s_packageHash}'; {PowerShellScript.Literal(Sums)} = '{s_sumsHash}' }})";
		ProcessResult run = await PowerShellScript.RunWithReleaseToolsAsync(
			$"Get-ReleaseAssetPlan -LocalAsset {local} -Release {release} -ReplaceableName {PowerShellScript.Literal(Tuple)} | " +
			"ConvertTo-Json -Compress");
		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		return JsonDocument.Parse(run.StandardOutput).RootElement.Clone();
	}

	private static string Release(bool isDraft, params (string Name, string Sha256)[] assets)
	{
		StringBuilder json = new();
		json.Append("{\"isDraft\":").Append(isDraft ? "true" : "false").Append(",\"assets\":[");
		for (int index = 0; index < assets.Length; index++)
		{
			if (index > 0)
			{
				json.Append(',');
			}

			json.Append("{\"name\":\"").Append(assets[index].Name).Append("\",\"digest\":\"sha256:")
				.Append(assets[index].Sha256).Append("\"}");
		}

		return json.Append("]}").ToString();
	}

	private static List<string> Strings(JsonElement plan, string property)
	{
		JsonElement value = plan.GetProperty(property);
		return value.ValueKind == JsonValueKind.Array
			? [.. value.EnumerateArray().Select(static e => e.GetString()!)]
			: [value.GetString()!];
	}
}
