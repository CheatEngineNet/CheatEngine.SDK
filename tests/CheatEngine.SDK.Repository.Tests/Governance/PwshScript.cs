using System.Diagnostics;
using System.Text;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     Runs the repository's PowerShell policy code in a separate <c>pwsh</c> process: <c>-NoProfile</c>,
///     <c>-NonInteractive</c>, the repository root as working directory, a 120 s limit and the test cancellation token.
///     The GitHub Actions file commands (<c>GITHUB_STEP_SUMMARY</c>, <c>GITHUB_OUTPUT</c>, ...), the tokens and every
///     input the governance scripts read from the environment are removed from the child environment, so a run inside CI
///     never writes into the job summary, reads the real pull request or reaches GitHub. A missing <c>pwsh</c> fails the
///     test: it is never skipped
///     (<c>--fail-skips on</c>).
/// </summary>
internal static class PwshScript
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(120);

	/// <summary>Variables that must come from the test, never from the process that runs the tests.</summary>
	private static readonly string[] s_isolatedVariables =
	[
		"GITHUB_STEP_SUMMARY", "GITHUB_OUTPUT", "GITHUB_ENV", "GITHUB_PATH", "GITHUB_STATE", "GITHUB_ACTIONS", "CI",
		"PR_TITLE", "PR_BODY", "PR_AUTHOR", "BASE_SHA", "HEAD_SHA", "GH_TOKEN", "GITHUB_TOKEN", "GH_REPO",
		"NEEDS", "RUN_URL", "DRIFT", "BROKEN_LINKS", "REPOSITORY", "SNAPSHOT_SHA", "SNAPSHOT_REF", "SNAPSHOT_CORRELATOR",
		"SNAPSHOT_JOB_ID", "SNAPSHOT_JOB_URL", "CESDK_PACKAGED_UMBRELLA_NUPKG"
	];

	/// <summary>Runs <paramref name="scriptText" /> from a temporary <c>.ps1</c> file.</summary>
	public static async Task<PwshResult> RunTextAsync(string scriptText,
		IReadOnlyDictionary<string, string>? environment = null)
	{
		using TemporaryDirectory directory = new();
		string scriptPath = Path.Combine(directory.Path, "script.ps1");
		string preamble = "Set-StrictMode -Version Latest" + Environment.NewLine +
						  "$ErrorActionPreference = 'Stop'" + Environment.NewLine;
		await File.WriteAllTextAsync(scriptPath, preamble + scriptText, new UTF8Encoding(false),
			TestContext.Current.CancellationToken);
		return await RunFileAsync(scriptPath, [], environment);
	}

	/// <summary>Runs a script file with arguments; a repository-relative path is resolved against the root.</summary>
	public static async Task<PwshResult> RunFileAsync(string scriptPath, IReadOnlyList<string> arguments,
		IReadOnlyDictionary<string, string>? environment = null)
	{
		string fullPath = Path.IsPathRooted(scriptPath) ? scriptPath : RepositoryFile.FullPath(scriptPath);
		ProcessStartInfo startInfo = new()
		{
			FileName = FindPwsh(),
			WorkingDirectory = RepositoryRoot.Path,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			CreateNoWindow = true
		};
		foreach (string argument in (string[]) ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", fullPath])
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (string name in s_isolatedVariables)
		{
			startInfo.Environment.Remove(name);
		}

		if (environment is not null)
		{
			foreach (KeyValuePair<string, string> variable in environment)
			{
				startInfo.Environment[variable.Key] = variable.Value;
			}
		}

		using CancellationTokenSource timeout =
			CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(s_timeout);

		using Process process = Process.Start(startInfo)
								?? throw new InvalidOperationException($"'{startInfo.FileName}' did not start.");
		process.StandardInput.Close();
		Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
		Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
			Assert.Fail($"pwsh did not finish '{RepositoryRoot.ToRelative(fullPath)}' within {s_timeout.TotalSeconds} s.");
		}

		return new PwshResult(process.ExitCode, await standardOutput, await standardError);
	}

	/// <summary>A single-quoted PowerShell string literal.</summary>
	public static string Quote(string value)
	{
		return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
	}

	private static string FindPwsh()
	{
		string fileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
		string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string directory in directories)
		{
			string candidate = Path.Combine(directory, fileName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		Assert.Fail(
			"PowerShell 7 (pwsh) is not on PATH. The governance tests run the repository's PowerShell policy code: install it " +
			"(winget install Microsoft.PowerShell, or https://learn.microsoft.com/powershell/scripting/install/installing-powershell) and rerun.");
		return fileName;
	}
}
