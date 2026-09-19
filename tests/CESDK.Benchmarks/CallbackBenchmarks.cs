using BenchmarkDotNet.Attributes;
using CESDK.Benchmarks.Support;
using CESDK.Lua.Calls;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Benchmarks;

/// <summary>
///     A callback round trip: <see cref="BenchFunctions.Touch(long)" /> is registered once
///     (<see cref="BenchFunctions.RegisterLuaFunctions" />) and then called from a Lua loop
///     <see cref="LoopCount" /> times inside one protected call, with <c>OperationsPerInvoke</c> set to
///     <see cref="LoopCount" /> so the reported time is per call.
/// </summary>
[MemoryDiagnoser(false)]
[ShortRunJob]
[BenchmarkCategory("Callbacks")]
public class CallbackBenchmarks : IDisposable
{
    /// <summary>Lua-side calls per invocation of <see cref="RoundTrip" />.</summary>
    private const int LoopCount = 1000;

    private LuaState _l;

    private NativeLuaState? _state;

    /// <inheritdoc />
    public void Dispose()
    {
        _ = BenchFunctions.UnregisterLuaFunctions(_l);
        LuaRuntime.Detach();
        _state?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Opens a state, registers the thunk and defines the Lua-side loop that calls it <see cref="LoopCount" /> times.</summary>
    [GlobalSetup]
    public void Setup()
    {
        NativeLuaLibrary.ThrowIfUnavailable();
        _state = new NativeLuaState();
        _l = FakeHostRuntime.Attach(_state, false);

        var registered = BenchFunctions.RegisterLuaFunctions(_l);
        if (!registered.IsOk)
            throw new InvalidOperationException("RegisterLuaFunctions failed: " + LuaError.FromStack(_l, registered));

        var defined = _l.TryExecute(
            "function cesdk_bench_loop(n) local s = 0 for i = 1, n do s = cesdk_bench_touch(s) end return s end"u8,
            0);
        if (!defined.IsOk)
            throw new InvalidOperationException("Defining cesdk_bench_loop failed: " + LuaError.FromStack(_l, defined));
    }

    /// <summary>
    ///     Unregisters the thunk, detaches the ambient runtime and closes the state. BenchmarkDotNet does not call
    ///     <see cref="Dispose" /> itself; this is what <c>[GlobalCleanup]</c> is for.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>One Lua loop of <see cref="LoopCount" /> calls to the registered thunk, inside one protected call.</summary>
    [Benchmark(OperationsPerInvoke = LoopCount)]
    public long RoundTrip()
    {
        var top = _l.Top;
        _ = _l.TryGetGlobal("cesdk_bench_loop"u8);
        _l.PushInteger(LoopCount);
        _ = _l.TryCall(1, 1);
        _ = _l.TryReadInteger(-1, out var result);
        _l.SetTop(top);
        return result;
    }
}
