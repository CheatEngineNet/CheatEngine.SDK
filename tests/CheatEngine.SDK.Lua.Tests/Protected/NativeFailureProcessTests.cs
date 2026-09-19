using System.Diagnostics;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.FailureBoundaries;

/// <summary>Crash-boundary checks run out of process so a regression cannot take down the test runner.</summary>
[Trait("Category", "NativeLua")]
public sealed class NativeFailureProcessTests
{
    [Fact]
    public async Task Allocation_failures_return_through_the_native_boundary()
    {
        Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);

        var baseDirectory = AppContext.BaseDirectory;
        var probe = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.FailureProbe.dll");
        var runtimeConfig = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.runtimeconfig.json");
        var depsFile = Path.Combine(baseDirectory, "CheatEngine.SDK.Lua.Tests.deps.json");
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

        using var process = Process.Start(start);
        Assert.NotNull(process);
        var cancellationToken = TestContext.Current.CancellationToken;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
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

        var standardOutput = await output;
        var standardError = await error;
        Assert.True(process.ExitCode == 0,
            $"Probe exit code: {process.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}");
        Assert.Contains("PASS native allocation and finalizer boundaries", standardOutput, StringComparison.Ordinal);
    }
}
