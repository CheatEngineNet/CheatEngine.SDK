namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>The outcome of one <see cref="ProcessRunner" /> invocation.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Everything the process wrote to standard output, newline-joined.</param>
/// <param name="StandardError">Everything the process wrote to standard error, newline-joined.</param>
internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Both streams, for an exception or assertion message: nothing to dig through separately.</summary>
    public string CombinedOutput => StandardOutput.Length == 0
        ? StandardError
        : StandardError.Length == 0
            ? StandardOutput
            : StandardOutput + Environment.NewLine + StandardError;
}
