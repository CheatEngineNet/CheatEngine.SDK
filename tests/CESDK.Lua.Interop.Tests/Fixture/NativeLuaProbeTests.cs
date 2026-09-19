using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Lua.Interop.Tests.Fixture;

/// <summary>
///     DLL-free: the lookup rules of the shared native Lua fixture, exercised only on paths that do not exist, so nothing
///     is loaded or bound and the process-wide <c>NativeLuaLibrary</c> is never touched (its constants are compile-time
///     literals).
/// </summary>
public sealed class NativeLuaProbeTests
{
    [Fact]
    public void Relative_configured_path_is_made_absolute_before_it_is_checked_and_reported()
    {
        var relative = Path.Combine("cesdk-no-such-directory", "lua53.dll");

        var probe = NativeLuaProbe.Run("  " + relative + "  ");

        // File.Exists resolves a relative path against the current directory, the native loader by its own search
        // rules: the probe must settle on one absolute path first, and that is the path the reason has to name.
        Assert.Equal(0, probe.Handle);
        Assert.Null(probe.LibraryPath);
        Assert.Contains("'" + Path.Combine(Environment.CurrentDirectory, relative) + "'", probe.Reason,
            StringComparison.Ordinal);
        Assert.Contains(NativeLuaLibrary.PathVariable, probe.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_configured_path_becomes_a_reason_not_an_exception()
    {
        var probe = NativeLuaProbe.Run("lua\0.dll");

        Assert.Equal(0, probe.Handle);
        Assert.Null(probe.LibraryPath);
        Assert.Contains("not a valid path", probe.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_absolute_path_is_reported_with_its_origin_and_never_falls_back()
    {
        var missing = Path.Combine(Path.GetTempPath(), "cesdk-no-such-directory", "lua53.dll");

        var probe = NativeLuaProbe.Run(missing);

        Assert.Equal(0, probe.Handle);
        Assert.Contains("'" + missing + "'", probe.Reason, StringComparison.Ordinal);
        Assert.Contains("does not exist", probe.Reason, StringComparison.Ordinal);
    }
}
