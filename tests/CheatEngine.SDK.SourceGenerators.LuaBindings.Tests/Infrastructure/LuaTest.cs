using System.Globalization;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>What the NativeLua tests share: the skip guard, chunk execution through the public API and result reading.</summary>
internal static class LuaTest
{
    /// <summary>Skips the calling test, with the fixture's reason, when no Lua 5.3 library is available.</summary>
    public static void RequireNativeLua()
    {
        Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);
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

    /// <summary>
    ///     Runs a chunk that returns one string and pops it, failing the test when the chunk raises or returns something
    ///     else.
    /// </summary>
    public static string RunForString(LuaState L, ReadOnlySpan<byte> source)
    {
        using LuaFrame frame = new(L);
        Run(L, source, 1);
        return ReadString(L, -1);
    }

    /// <summary>Runs a chunk that returns one integer and pops it.</summary>
    public static long RunForInteger(LuaState L, ReadOnlySpan<byte> source)
    {
        using LuaFrame frame = new(L);
        Run(L, source, 1);
        Assert.True(L.TryReadInteger(-1, out var value), "The chunk returned a " + L.TypeOf(-1) + ", not an integer.");
        return value;
    }

    /// <summary>
    ///     Runs a chunk of the form <c>return pcall(...)</c>, asserts that the protected call failed and returns its
    ///     error message.
    /// </summary>
    public static string RunForError(LuaState L, ReadOnlySpan<byte> source)
    {
        using LuaFrame frame = new(L);
        Run(L, source, 2);
        Assert.Equal(LuaType.Boolean, L.TypeOf(-2));
        Assert.False(L.ToBoolean(-2), "The protected call succeeded, an error was expected.");
        return ReadString(L, -1);
    }

    /// <summary>Reads the string at <paramref name="index" />, or fails the test when the value is not a string.</summary>
    public static string ReadString(LuaState L, int index)
    {
        Assert.True(L.TryReadString(index, out var value),
            string.Create(CultureInfo.InvariantCulture,
                $"The value at {index} is a {L.TypeOf(index)}, not a string."));
        return value;
    }
}
