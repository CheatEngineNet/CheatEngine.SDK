using System.Diagnostics;
using System.Text;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     Runs PowerShell 7 (<c>pwsh</c>) for the tests of the repository scripts. <c>pwsh</c> is resolved from <c>PATH</c>;
///     when it is absent the test fails instead of being skipped, because CI runs with <c>--fail-skips on</c> and a
///     silently skipped script test would hide a broken gate. It never starts Cheat Engine.
/// </summary>
internal static class PowerShellProcess
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

	internal static string Executable { get; } = Resolve();

	/// <summary>Runs <paramref name="script" /> with <c>-NoProfile -NonInteractive -EncodedCommand</c>.</summary>
	internal static Result RunCommand(string script, IReadOnlyDictionary<string, string?>? environment,
		CancellationToken cancellationToken)
	{
		string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		return Run(["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], environment, cancellationToken);
	}

	/// <summary>Runs a script file with <c>-NoProfile -NonInteractive -File</c> and the given arguments.</summary>
	internal static Result RunFile(string file, IReadOnlyList<string> arguments,
		IReadOnlyDictionary<string, string?>? environment, CancellationToken cancellationToken)
	{
		return Run(["-NoProfile", "-NonInteractive", "-File", file, .. arguments], environment, cancellationToken);
	}

	/// <summary>A PowerShell string literal (single-quoted) for <paramref name="value" />.</summary>
	internal static string Quote(string value)
	{
		return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
	}

	private static Result Run(IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment,
		CancellationToken cancellationToken)
	{
		ProcessStartInfo start = new(Executable)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			CreateNoWindow = true,
			WorkingDirectory = Infrastructure.RepositoryRoot.Path
		};
		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		if (environment is not null)
		{
			foreach ((string name, string? value) in environment)
			{
				if (value is null)
				{
					start.Environment.Remove(name);
				}
				else
				{
					start.Environment[name] = value;
				}
			}
		}

		using Process process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		if (!process.WaitForExit(Timeout))
		{
			process.Kill(true);
			throw new TimeoutException($"pwsh did not exit within {Timeout.TotalSeconds} seconds.");
		}

		process.WaitForExit();
		return new Result(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
	}

	private static string Resolve()
	{
		string[] names = OperatingSystem.IsWindows() ? ["pwsh.exe"] : ["pwsh"];
		foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(
					 Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			foreach (string name in names)
			{
				string candidate = Path.Combine(directory.Trim('"'), name);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		throw new InvalidOperationException(
			"PowerShell 7 (pwsh) is not on PATH. The repository script tests require it; install PowerShell 7.4 or later.");
	}

	/// <summary>The exit code and the captured output of one run.</summary>
	internal sealed record Result(int ExitCode, string Output, string Error)
	{
		/// <summary>Everything the process printed, for assertion messages.</summary>
		internal string Transcript => $"exit {ExitCode}{Environment.NewLine}{Output}{Environment.NewLine}{Error}";
	}
}
