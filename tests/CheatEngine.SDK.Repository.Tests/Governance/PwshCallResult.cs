using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     The outcome of a <see cref="PwshCall" />: every object the function wrote, as JSON (always an array), or the
///     message of the error it threw.
/// </summary>
internal sealed record PwshCallResult(JsonElement Output, string? Error)
{
	/// <summary>The single object the function wrote; fails the test on an error or another count.</summary>
	public JsonElement Single(string name)
	{
		Assert.True(Error is null, $"{name} threw: {Error}");
		Assert.True(Output.GetArrayLength() == 1, $"{name} wrote {Output.GetArrayLength()} objects, expected one: {Output}");
		return Output[0];
	}

	/// <summary>The objects the function wrote; fails the test on an error.</summary>
	public List<JsonElement> Items(string name)
	{
		Assert.True(Error is null, $"{name} threw: {Error}");
		return [.. Output.EnumerateArray()];
	}
}
