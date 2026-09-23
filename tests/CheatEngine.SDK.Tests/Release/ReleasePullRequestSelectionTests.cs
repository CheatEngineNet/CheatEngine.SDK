using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <c>Select-ReleasePullRequest</c> names the pull request the release tuple records for the released commit, from the
///     GitHub answer to <c>GET /repos/{repo}/commits/{sha}/pulls</c>: the pull request squash-merged as a tagged commit, or
///     on a dry run the single pull request headed by the branch commit. It never guesses: an ambiguous or unrelated
///     answer records no pull request.
/// </summary>
public sealed class ReleasePullRequestSelectionTests
{
	private static readonly string s_commit = new('a', 40);
	private static readonly string s_head = new('b', 40);
	private static readonly string s_otherHead = new('c', 40);

	[Fact]
	public async Task Pull_request_squash_merged_as_the_tagged_commit_is_selected()
	{
		string answer = $"[{PullRequest(12, s_head, merged: true, mergeCommit: s_commit)}," +
						$"{PullRequest(13, s_commit, merged: false, mergeCommit: null)}]";

		JsonElement selected = await SelectAsync(answer);

		Assert.Equal("12", selected.GetProperty("Number").GetString());
		Assert.Equal(s_head, selected.GetProperty("HeadSha").GetString());
		Assert.True(selected.GetProperty("Merged").GetBoolean());
	}

	[Fact]
	public async Task Dry_run_commit_is_matched_to_the_single_pull_request_it_heads()
	{
		JsonElement selected = await SelectAsync($"[{PullRequest(86, s_commit, merged: false, mergeCommit: new string('d', 40))}]");

		Assert.Equal("86", selected.GetProperty("Number").GetString());
		Assert.Equal(s_commit, selected.GetProperty("HeadSha").GetString());
		Assert.False(selected.GetProperty("Merged").GetBoolean());
	}

	[Theory]
	[InlineData("[]")]
	[InlineData("unrelated")]
	[InlineData("two-heads")]
	[InlineData("merged-elsewhere")]
	public async Task Absent_ambiguous_or_unrelated_pull_requests_record_none(string shape)
	{
		string answer = shape switch
		{
			"unrelated" => $"[{PullRequest(1, s_otherHead, merged: true, mergeCommit: new string('e', 40))}]",
			"two-heads" => $"[{PullRequest(2, s_commit, merged: false, mergeCommit: null)},{PullRequest(3, s_commit, merged: false, mergeCommit: null)}]",
			"merged-elsewhere" => $"[{PullRequest(4, s_head, merged: true, mergeCommit: s_otherHead)}]",
			_ => shape
		};

		ProcessResult run = await RunAsync(answer);

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		Assert.Equal("", run.StandardOutput.Trim());
	}

	private static async Task<JsonElement> SelectAsync(string answer)
	{
		ProcessResult run = await RunAsync(answer);
		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		return JsonDocument.Parse(run.StandardOutput).RootElement.Clone();
	}

	private static Task<ProcessResult> RunAsync(string answer)
	{
		return PowerShellScript.RunWithReleaseToolsAsync(
			$"$found = Select-ReleasePullRequest -PullRequest @({PowerShellScript.Literal(answer)} | ConvertFrom-Json) " +
			$"-Commit {PowerShellScript.Literal(s_commit)}; if ($null -ne $found) {{ $found | ConvertTo-Json -Compress }}");
	}

	private static string PullRequest(int number, string head, bool merged, string? mergeCommit)
	{
		string mergedAt = merged ? "\"2026-09-23T00:00:00Z\"" : "null";
		string mergeCommitSha = mergeCommit is null ? "null" : $"\"{mergeCommit}\"";
		return $"{{\"number\":{number},\"merged_at\":{mergedAt},\"merge_commit_sha\":{mergeCommitSha},\"head\":{{\"sha\":\"{head}\"}}}}";
	}
}
