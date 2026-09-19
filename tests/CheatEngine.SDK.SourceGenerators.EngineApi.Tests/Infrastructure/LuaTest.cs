using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

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
}
