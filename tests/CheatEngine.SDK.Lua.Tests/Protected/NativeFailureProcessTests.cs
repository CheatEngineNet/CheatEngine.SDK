using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

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
	[Trait("Qualification", "Q13")]
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

	/// <summary>
	///     Data-driven from <c>libs/CheatEngine.SDK.Lua.Interop/Protected/protected-operations.json</c>: every operation that can raise names its
	///     failure evidence, and every probe marker it names is printed by one run of the failure probe. The probe prints a
	///     marker only after the failure returned a status and the Lua stack was restored, so a missing marker is a
	///     failure path that no longer recovers. Adding a raising operation without evidence fails here.
	/// </summary>
	[Fact]
	[Trait("Qualification", "Q13")]
	public async Task Every_catalogued_raising_operation_reports_its_failure_marker()
	{
		Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);
		List<string> markers = [];
		using (JsonDocument catalogue = LoadCatalogue())
		{
			foreach (JsonElement operation in catalogue.RootElement.GetProperty("operations").EnumerateArray())
			{
				string id = operation.GetProperty("id").GetString()!;
				if (string.Equals(operation.GetProperty("raises").GetString(), "never", StringComparison.Ordinal))
				{
					continue;
				}

				Assert.True(operation.TryGetProperty("failureEvidence", out JsonElement evidence) &&
							evidence.GetArrayLength() > 0,
					$"Protected operation {id} can raise but names no failure evidence.");
				foreach (JsonElement item in evidence.EnumerateArray())
				{
					if (string.Equals(item.GetProperty("kind").GetString(), "FailureProbeMarker", StringComparison.Ordinal))
					{
						markers.Add(item.GetProperty("marker").GetString()!);
					}
				}
			}
		}

		Assert.NotEmpty(markers);
		string standardOutput = await RunFailureProbeAsync();

		string[] missing = [.. markers.Where(marker => !standardOutput.Contains(marker, StringComparison.Ordinal))];
		Assert.True(missing.Length == 0,
			$"The failure probe did not print: {string.Join(" | ", missing)}{Environment.NewLine}stdout:{Environment.NewLine}{standardOutput}");
	}

	private static JsonDocument LoadCatalogue()
	{
		const string ResourceName = "CheatEngine.SDK.Lua.Tests.ProtectedOperations.json";
		using Stream stream = typeof(NativeFailureProcessTests).Assembly.GetManifestResourceStream(ResourceName)
							  ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
		return JsonDocument.Parse(stream);
	}

	/// <summary>Runs the failure probe's default battery in a child process and returns its standard output.</summary>
	private static async Task<string> RunFailureProbeAsync()
	{
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
		return standardOutput;
	}
}
