using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Reads a file as committed in <c>HEAD</c>, not as it sits in the working tree. CI overwrites the checked-in native
///     bridge with the one it just built before building, so only the committed blob is the audited asset. The bytes are
///     copied from the binary standard output stream: <see cref="ProcessRunner" /> decodes text and would corrupt a DLL.
/// </summary>
internal static class CommittedFile
{
	private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

	/// <summary>The exact bytes of <c>HEAD:&lt;repoRelativePath&gt;</c>.</summary>
	/// <param name="repoRelativePath">A repository-relative path with forward slashes.</param>
	/// <exception cref="InvalidOperationException">git is missing, the tree is not a git checkout, or the path is not committed.</exception>
	public static async Task<byte[]> ReadBytesAsync(string repoRelativePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(repoRelativePath);
		ProcessStartInfo startInfo = new("git")
		{
			WorkingDirectory = RepositoryLayout.Root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("-C");
		startInfo.ArgumentList.Add(RepositoryLayout.Root);
		startInfo.ArgumentList.Add("cat-file");
		startInfo.ArgumentList.Add("blob");
		startInfo.ArgumentList.Add($"HEAD:{repoRelativePath}");

		using Process process = new()
		{
			StartInfo = startInfo
		};
		try
		{
			process.Start();
		}
		catch (Win32Exception exception)
		{
			throw new InvalidOperationException(
				$"Reading the committed '{repoRelativePath}' requires a git checkout and git on PATH.", exception);
		}

		using CancellationTokenSource cancellation = new(Timeout);
		using MemoryStream blob = new();
		Task copy = process.StandardOutput.BaseStream.CopyToAsync(blob, cancellation.Token);
		Task<string> errorRead = process.StandardError.ReadToEndAsync(cancellation.Token);
		string error;
		try
		{
			await copy.ConfigureAwait(false);
			error = await errorRead.ConfigureAwait(false);
			await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException exception) when (cancellation.IsCancellationRequested)
		{
			process.Kill(true);
			throw new TimeoutException($"'git cat-file blob HEAD:{repoRelativePath}' did not exit within {Timeout}.", exception);
		}

		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"Reading the committed '{repoRelativePath}' requires a git checkout that contains it: 'git cat-file' " +
				$"exited with {process.ExitCode.ToString(CultureInfo.InvariantCulture)}: {error.Trim()}");
		}

		return blob.ToArray();
	}
}
