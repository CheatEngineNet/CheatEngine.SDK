namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>The outcome of one <c>pwsh</c> process.</summary>
internal sealed record PwshResult(int ExitCode, string StandardOutput, string StandardError)
{
	/// <summary>Both streams, for assertion messages.</summary>
	public string Transcript => $"exit code {ExitCode}{Environment.NewLine}--- stdout ---{Environment.NewLine}{StandardOutput}{Environment.NewLine}--- stderr ---{Environment.NewLine}{StandardError}";
}
