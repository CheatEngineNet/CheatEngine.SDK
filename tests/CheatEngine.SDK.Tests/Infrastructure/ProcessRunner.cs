using System.Diagnostics;
using System.Text;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Runs <c>dotnet</c> as a child process with both output streams captured, for the packing/restoring/building
///     pipeline <see cref="PackagedUmbrellaFixture" /> drives against the real .NET SDK - the only way to observe what a
///     plugin author's own <c>dotnet build</c> actually does with the packed umbrella package.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>Starts <paramref name="fileName" />, waits up to <paramref name="timeout" />, and returns its output.</summary>
    /// <exception cref="TimeoutException">The process did not exit in time; it is killed (with its child tree) first.</exception>
    public static async Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory,
        TimeSpan timeout)
    {
        ProcessStartInfo startInfo = new(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo };
        StringBuilder standardOutput = new();
        StringBuilder standardError = new();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) standardOutput.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) standardError.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenSource cancellation = new(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"'{fileName} {arguments}' in '{workingDirectory}' did not exit within {timeout}. Output so far:{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }

        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout firing and the kill: nothing left to do.
        }
    }
}
