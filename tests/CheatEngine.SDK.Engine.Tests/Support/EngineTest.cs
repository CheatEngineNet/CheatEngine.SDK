using System.Globalization;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Support;

/// <summary>What the NativeLua tests share: the skip guard, chunk execution through the public API and error reading.</summary>
internal static class EngineTest
{
    /// <summary>
    ///     Whether this assembly, and therefore the SDK it was built with, is a Debug build: the configuration in which
    ///     <c>Debug.Assert</c> guards exist. Tests of a Debug-only guard skip in Release.
    /// </summary>
#if DEBUG
    public const bool IsDebugBuild = true;
#else
    public const bool IsDebugBuild = false;
#endif

    /// <summary>Skips the calling test, with the fixture's reason, when no Lua 5.3 library is available.</summary>
    public static void RequireNativeLua()
    {
        Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);
    }

    /// <summary>
    ///     Runs <paramref name="work" /> on a fresh thread, with Debug assertions turned into exceptions, and returns what
    ///     it threw (<see langword="null" /> when it completed). The calling thread waits for it, so nothing runs
    ///     concurrently.
    /// </summary>
    public static Exception? RunOnWorker(Action work)
    {
        Exception? failure = null;
        using DebugAssertScope guard = new();
        Thread worker = new(() =>
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        worker.Start();
        worker.Join();
        return failure;
    }

    /// <summary>Wraps the fixture's state in the SDK view.</summary>
    public static LuaState View(NativeLuaState state)
    {
        return new LuaState(state.Pointer);
    }

    /// <summary>Compiles and runs a chunk, failing the test with the Lua message on any error. Results stay on the stack.</summary>
    public static void Run(LuaState L, ReadOnlySpan<byte> source, int resultCount = 0)
    {
        var status = L.TryExecute(source, resultCount, "=test"u8);
        if (!status.IsOk)
        {
            var error = LuaError.FromStack(L, status);
            Assert.Fail("The chunk failed: " + error);
        }
    }

    /// <summary>Reads the error value on top after a failed status, without popping it.</summary>
    public static string ErrorMessage(LuaState L, LuaStatus status)
    {
        Assert.False(status.IsOk, "The operation succeeded; there is no error to read.");
        return LuaError.FromStack(L, status).Message;
    }

    /// <summary>Reads the string at <paramref name="index" />, or fails the test when the value is not a string.</summary>
    public static string ReadString(LuaState L, int index)
    {
        Assert.True(L.TryReadString(index, out var value),
            "The value at " + index.ToString(CultureInfo.InvariantCulture) + " is a " + L.TypeOf(index) +
            ", not a string.");
        return value;
    }

    /// <summary>Reads the integer at <paramref name="index" />, or fails the test when the value is not an integer.</summary>
    public static long ReadInteger(LuaState L, int index)
    {
        Assert.True(L.TryReadInteger(index, out var value),
            "The value at " + index.ToString(CultureInfo.InvariantCulture) + " is a " + L.TypeOf(index) +
            ", not an integer.");
        return value;
    }
}
