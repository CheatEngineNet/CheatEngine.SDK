using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     The advisory client-canary script (audit Q48, ADR-10): its report matches its schema, it never touches the global
///     package cache or the Client's committed lock files, and it never fails the run because the Client breaks.
/// </summary>
public sealed partial class ClientCanaryScriptTests
{
	private const string ScriptPath = "eng/ci/Invoke-ClientCanary.ps1";
	private const string SchemaPath = "eng/ci/client-canary-report.v0.schema.json";

	private static readonly string[] s_reportFields =
	[
		"schema", "sdkPackage", "client", "properties", "restoreExitCode", "buildExitCode", "outcome", "errorCount", "errors",
		"createdUtc"
	];

	private static readonly string[] s_errorFields = ["code", "file", "line", "message"];

	[Fact]
	public void Client_canary_report_schema_requires_exactly_the_fields_the_script_writes()
	{
		using JsonDocument schema = JsonDocument.Parse(ReadRepositoryText(SchemaPath));
		JsonElement root = schema.RootElement;
		Assert.Equal($"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/{SchemaPath}", root.GetProperty("$id").GetString());
		Assert.Equal(s_reportFields, Strings(root.GetProperty("required")), StringComparer.Ordinal);
		Assert.False(root.GetProperty("additionalProperties").GetBoolean());
		JsonElement error = root.GetProperty("properties").GetProperty("errors").GetProperty("items");
		Assert.Equal(s_errorFields, Strings(error.GetProperty("required")), StringComparer.Ordinal);
		Assert.False(error.GetProperty("additionalProperties").GetBoolean());

		string script = ReadRepositoryText(ScriptPath);
		foreach (string field in s_reportFields)
		{
			Assert.True(Regex.IsMatch(script, $@"(?m)^\s*{field} = ", RegexOptions.None, TimeSpan.FromSeconds(1)),
				$"{ScriptPath} does not write '{field}'.");
		}

		Assert.Contains("[ordered]@{ code = $code; file = $file; line = $lineNumber; message = $message }", script, StringComparison.Ordinal);
		Assert.Contains("$reportSchema = 'cheatengine-client-canary-report/v0'", script, StringComparison.Ordinal);
	}

	[Fact]
	public void Client_canary_isolates_its_packages_and_never_gates()
	{
		string script = StripComments(ReadRepositoryText(ScriptPath));

		// The branch package reaches only an isolated package folder, through a feed mapped to CheatEngine.SDK alone.
		Assert.Contains("$env:NUGET_PACKAGES = $packages", script, StringComparison.Ordinal);
		Assert.Contains("<package pattern=\"CheatEngine.SDK\" />", script, StringComparison.Ordinal);
		Assert.Contains("<clear />", script, StringComparison.Ordinal);

		// Lock files are re-evaluated in the throw-away checkout; RestorePackagesWithLockFile=false would fail with NU1005.
		Assert.Contains("'--force-evaluate'", script, StringComparison.Ordinal);
		Assert.Contains("'RestoreLockedMode=false'", script, StringComparison.Ordinal);
		Assert.DoesNotContain("RestorePackagesWithLockFile=false", script, StringComparison.Ordinal);

		// A broken Client is the report, not a failure: the script ends with exit 0.
		Assert.EndsWith("exit 0", script.TrimEnd(), StringComparison.Ordinal);
	}

	private static string ReadRepositoryText(string relativePath)
	{
		string path = Path.Combine(RepositoryRoot.Path, relativePath);
		Assert.True(File.Exists(path), $"{relativePath} is missing.");
		return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	private static List<string> Strings(JsonElement array)
	{
		List<string> values = [];
		foreach (JsonElement item in array.EnumerateArray())
		{
			values.Add(item.GetString() ?? "");
		}

		return values;
	}

	private static string StripComments(string script)
	{
		List<string> lines = [];
		foreach (string line in CommentBlock().Replace(script, "").Split('\n'))
		{
			if (!line.TrimStart().StartsWith('#'))
			{
				lines.Add(line);
			}
		}

		return string.Join('\n', lines);
	}

	[GeneratedRegex(@"<#.*?#>", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex CommentBlock();
}
