namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     Every PowerShell script and module of the governance and health tooling parses. PSScriptAnalyzer reports syntax
///     errors with the separate <c>ParseError</c> severity, which a <c>Severity = Error, Warning</c> profile filters out, and
///     most of these scripts only run weekly or after merge, so a syntax error would otherwise surface in production.
/// </summary>
public sealed class GovernanceScriptSyntaxTests
{
	private static readonly string[] s_roots = ["eng/ci", "eng/github"];

	[Fact]
	public async Task Every_governance_script_parses_without_errors()
	{
		List<string> scripts = [];
		foreach (string root in s_roots)
		{
			string full = RepositoryFile.FullPath(root);
			if (!Directory.Exists(full))
			{
				continue;
			}

			foreach (string pattern in (string[]) ["*.ps1", "*.psm1"])
			{
				foreach (string file in Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories))
				{
					scripts.Add(file);
				}
			}
		}

		Assert.NotEmpty(scripts);
		string list = string.Join(", ", scripts.ConvertAll(PwshScript.Quote));
		string script = $$"""
			$problems = foreach ($path in @({{list}})) {
			    $tokens = $null
			    $errors = $null
			    [void] [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
			    foreach ($parseError in $errors) {
			        "$($path):$($parseError.Extent.StartLineNumber): $($parseError.Message)"
			    }
			}
			$problems | ForEach-Object { Write-Output $_ }
			""";

		PwshResult run = await PwshScript.RunTextAsync(script);

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.True(string.IsNullOrWhiteSpace(run.StandardOutput), $"PowerShell syntax errors:{Environment.NewLine}{run.StandardOutput}");
	}
}
