using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Runs the release scripts of <c>eng/release</c> with PowerShell 7 from the repository root, the way the release
///     workflow runs them. The GitHub Actions file commands (<c>GITHUB_STEP_SUMMARY</c>, <c>GITHUB_OUTPUT</c>, ...) are
///     removed from the child environment, so a test run inside a CI job never writes into that job's summary or outputs.
/// </summary>
internal static class PowerShellScript
{
	private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

	private static readonly string[] s_fileCommandVariables =
		["GITHUB_STEP_SUMMARY", "GITHUB_OUTPUT", "GITHUB_ENV", "GITHUB_PATH", "GITHUB_STATE"];

	/// <summary>Absolute path of <c>eng/release/ReleaseTools.psm1</c>.</summary>
	public static string ReleaseToolsModule => RepositoryLayout.PathOf("eng/release/ReleaseTools.psm1");

	/// <summary>Runs <c>pwsh -File &lt;script&gt; &lt;arguments&gt;</c>; every argument is passed verbatim.</summary>
	public static Task<ProcessResult> RunFileAsync(string scriptRelativePath, params string[] arguments)
	{
		List<string> commandLine = ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", RepositoryLayout.PathOf(scriptRelativePath)];
		commandLine.AddRange(arguments);
		return RunAsync(commandLine);
	}

	/// <summary>
	///     Runs PowerShell code after importing <see cref="ReleaseToolsModule" />; the code's output is the standard output.
	///     Build literals with <see cref="Literal" />.
	/// </summary>
	public static Task<ProcessResult> RunWithReleaseToolsAsync(string command)
	{
		string script = "$ErrorActionPreference = 'Stop'; Set-StrictMode -Version Latest; " +
						$"Import-Module {Literal(ReleaseToolsModule)}; {command}";
		string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		return RunAsync(["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]);
	}

	/// <summary>A single-quoted PowerShell string literal of <paramref name="value" />.</summary>
	public static string Literal(string value)
	{
		return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
	}

	private static ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
	{
		ProcessStartInfo startInfo = new("pwsh")
		{
			WorkingDirectory = RepositoryLayout.Root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (string variable in s_fileCommandVariables)
		{
			startInfo.Environment.Remove(variable);
		}

		startInfo.Environment["NO_COLOR"] = "1";
		return startInfo;
	}

	private static async Task<ProcessResult> RunAsync(IEnumerable<string> arguments)
	{
		ProcessStartInfo startInfo = CreateStartInfo(arguments);
		using Process process = new()
		{
			StartInfo = startInfo
		};
		StringBuilder standardOutput = new();
		StringBuilder standardError = new();
		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data is not null)
			{
				standardOutput.AppendLine(e.Data);
			}
		};
		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is not null)
			{
				standardError.AppendLine(e.Data);
			}
		};

		try
		{
			process.Start();
		}
		catch (Win32Exception exception)
		{
			throw new InvalidOperationException(
				"pwsh 7 is required on PATH to test the release scripts: " +
				"https://learn.microsoft.com/powershell/scripting/install/installing-powershell", exception);
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		using CancellationTokenSource cancellation = new(Timeout);
		try
		{
			await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			process.Kill(true);
			throw new TimeoutException($"pwsh did not exit within {Timeout}. Output so far:{Environment.NewLine}{standardOutput}{standardError}");
		}

		return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
	}
}
