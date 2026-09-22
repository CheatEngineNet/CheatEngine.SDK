using System.Diagnostics;
using System.Globalization;

using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Protected;

/// <summary>
///     Runs the direct managed <c>lua_checkstack</c> growth path in a child process. A failure of Lua's internal
///     protection must not be allowed to terminate the native-Lua test host.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class CheckStackFailureProcessTests
{
	[Fact]
	public async Task Direct_checkstack_growth_with_a_rejecting_allocator_returns_zero_and_recovers()
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
		start.ArgumentList.Add("--checkstack-growth");

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
			Assert.Fail("The direct lua_checkstack failure probe did not exit within 30 seconds.");
		}

		string standardOutput = await output;
		string standardError = await error;
		Assert.True(process.ExitCode == 0,
			string.Create(CultureInfo.InvariantCulture,
				$"Probe exit code: {process.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}"));
		AssertMarkers(standardOutput,
			"MARK lua_checkstack-direct-growth-before-reject",
			"MARK lua_checkstack-direct-growth-returned-zero",
			"MARK lua_checkstack-direct-growth-recovery-reserved",
			"MARK lua_checkstack-bridge-fill-before-reject",
			"MARK lua_checkstack-bridge-stack-full",
			"MARK lua_checkstack-bridge-returned-no-error-status",
			"MARK lua_checkstack-bridge-stack-restored",
			"MARK lua_pushuncheckedfunction-fill-before-reject",
			"MARK lua_pushuncheckedfunction-stack-full",
			"MARK lua_pushuncheckedfunction-reservation-rejected",
			"MARK lua_pushuncheckedfunction-stack-restored",
			"PASS lua_checkstack direct rejected-growth returns 0 and the state recovers");
	}

	private static void AssertMarkers(string standardOutput, params string[] markers)
	{
		for (int index = 0; index < markers.Length; index++)
		{
			Assert.Contains(markers[index], standardOutput, StringComparison.Ordinal);
		}
	}
}
