using System.Diagnostics;
using System.Globalization;

using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Protected;

/// <summary>Runs bounded native protected-operation fuzzing out of process so native regressions cannot stop MTP.</summary>
[Trait("Category", "NativeLua")]
public sealed class NativeFuzzProcessTests
{
	[Fact]
	public async Task Protected_operation_fuzz_sequences_preserve_results_stack_and_recovery()
	{
		Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);
		string baseDirectory = AppContext.BaseDirectory;
		string probe = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.FailureProbe.dll");
		Assert.True(File.Exists(probe), $"Failure probe was not copied to '{probe}'.");

		ProcessStartInfo start = new("dotnet")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		start.ArgumentList.Add("exec");
		start.ArgumentList.Add("--runtimeconfig");
		start.ArgumentList.Add(Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.runtimeconfig.json"));
		start.ArgumentList.Add("--depsfile");
		start.ArgumentList.Add(Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.deps.json"));
		start.ArgumentList.Add(probe);
		start.ArgumentList.Add(NativeLuaLibrary.LibraryPath!);
		start.ArgumentList.Add("--fuzz-protected-operations");

		using Process? process = Process.Start(start);
		Assert.NotNull(process);
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		// Pipe lifetime follows the owned process: cancellation kills it, then drains both streams before disposal.
		Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(60));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			await StopProcessAsync(process);
			string[] captured = await Task.WhenAll(output, error);
			if (cancellationToken.IsCancellationRequested)
			{
				throw;
			}

			Assert.Fail($"The protected-operation fuzz probe did not exit within 60 seconds.\nstdout:\n{captured[0]}\nstderr:\n{captured[1]}");
		}

		string standardOutput = await output;
		string standardError = await error;
		Assert.True(process.ExitCode == 0,
			string.Create(CultureInfo.InvariantCulture,
				$"Probe exit code: {process.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}"));
		Assert.Contains("PASS protected fuzz seed=0xD1CEBA5E sequences=2000 protected-api-calls=20000", standardOutput,
			StringComparison.Ordinal);
		Assert.Contains("buffers<=1024 capacities<=64 allocator-refusals=2000", standardOutput,
			StringComparison.Ordinal);
	}

	private static async Task StopProcessAsync(Process process)
	{
		if (!process.HasExited)
		{
			try
			{
				process.Kill(true);
			}
			catch (InvalidOperationException) when (process.HasExited)
			{
				// It exited between the status check and the kill request; drain it below.
			}
		}

		await process.WaitForExitAsync(CancellationToken.None);
	}
}
