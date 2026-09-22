using System.Diagnostics;
using System.Globalization;

using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Protected;

/// <summary>Crash-boundary checks run out of process so a regression cannot take down the test runner.</summary>
[Trait("Category", "NativeLua")]
public sealed class NativeFailureProcessTests
{
	[Fact]
	[Trait("Qualification", "Q12")]
	public async Task Generated_function_PushClosure_failure_returns_status_and_restores_stack()
	{
		Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);

		string baseDirectory = AppContext.BaseDirectory;
		string probe = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.FailureProbe.dll");
		string runtimeConfig = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.runtimeconfig.json");
		string depsFile = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.deps.json");
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
		start.ArgumentList.Add(runtimeConfig);
		start.ArgumentList.Add("--depsfile");
		start.ArgumentList.Add(depsFile);
		start.ArgumentList.Add(probe);
		start.ArgumentList.Add(NativeLuaLibrary.LibraryPath!);
		start.ArgumentList.Add("--generated-function-allocation");

		using Process? process = Process.Start(start);
		Assert.NotNull(process);
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(30));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			process.Kill(true);
			Assert.Fail("The generated-function allocation probe did not exit within 30 seconds.");
		}

		string standardOutput = await output;
		string standardError = await error;
		Assert.True(process.ExitCode == 0,
			string.Create(CultureInfo.InvariantCulture,
				$"Probe exit code: {process.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}"));
		Assert.Contains("MARK TryPushGeneratedFunction PushClosure status, stack, and ownership recovered",
			standardOutput,
			StringComparison.Ordinal);
		Assert.Contains("PASS generated function closure allocation failure returns status and restores stack",
			standardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Allocation_failures_return_through_the_native_boundary()
	{
		Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);

		string baseDirectory = AppContext.BaseDirectory;
		string probe = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.FailureProbe.dll");
		string runtimeConfig = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.runtimeconfig.json");
		string depsFile = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.deps.json");
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
		start.ArgumentList.Add(runtimeConfig);
		start.ArgumentList.Add("--depsfile");
		start.ArgumentList.Add(depsFile);
		start.ArgumentList.Add(probe);
		start.ArgumentList.Add(NativeLuaLibrary.LibraryPath!);

		using Process? process = Process.Start(start);
		Assert.NotNull(process);
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(30));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			process.Kill(true);
			Assert.Fail("The native failure probe did not exit within 30 seconds.");
		}

		string standardOutput = await output;
		string standardError = await error;
		Assert.True(process.ExitCode == 0,
			string.Create(CultureInfo.InvariantCulture,
				$"Probe exit code: {process.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}"));
		Assert.Contains("MARK PushByteTable protected allocator boundary recovered", standardOutput,
			StringComparison.Ordinal);
		Assert.Contains("MARK PushHostObject native pusher longjmp observed", standardOutput, StringComparison.Ordinal);
		Assert.Contains("MARK LuaRef.Release protected allocator boundary recovered", standardOutput,
			StringComparison.Ordinal);
		Assert.Contains("PASS native protected allocation, finalizer, and host-object longjmp boundaries",
			standardOutput,
			StringComparison.Ordinal);
	}
}
