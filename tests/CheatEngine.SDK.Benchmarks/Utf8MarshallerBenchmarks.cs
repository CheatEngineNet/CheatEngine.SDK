using BenchmarkDotNet.Attributes;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Benchmarks;

/// <summary>
///     Measures UTF-8 byte marshalling at payload sizes that distinguish a short identifier from larger CE text. The
///     input is a valid repeated four-byte UTF-8 sequence held in a field, so neither setup nor constant folding appears
///     in the measured operation.
/// </summary>
/// <remarks>
///     This is a raw <see cref="Utf8Marshaller" /> benchmark: it measures push plus borrowed-span read, not UTF-16
///     conversion. <c>StringMarshaller</c>'s allocating read remains covered separately by
///     <see cref="MarshallerBenchmarks.PushReadString" />.
/// </remarks>
[MemoryDiagnoser(false)]
[BenchmarkCategory("Transition", "Utf8")]
public class Utf8MarshallerBenchmarks : IDisposable
{
    private LuaState _state;

    private NativeLuaState? _nativeState;

    /// <summary>Number of UTF-8 bytes in the valid, non-ASCII payload.</summary>
    [Params(16, 64, 1024)]
    public int ByteCount { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        _nativeState?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Builds a deterministic valid UTF-8 payload and opens an independent fixture state.</summary>
    [GlobalSetup]
    public void Setup()
    {
        NativeLuaLibrary.ThrowIfUnavailable();
        _nativeState = new NativeLuaState();
        _state = new LuaState(_nativeState.Pointer);
        Payload = new byte[ByteCount];

        for (var index = 0; index < Payload.Length; index += 4)
        {
            Payload[index] = 0xF0;
            Payload[index + 1] = 0x9F;
            Payload[index + 2] = 0xA7;
            Payload[index + 3] = 0xAA;
        }
    }

    /// <summary>Closes the fixture state after the parameter case completes.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>Pushes and reads the current valid UTF-8 payload, returning the borrowed span length.</summary>
    [Benchmark(Baseline = true)]
    public int PushReadUtf8()
    {
        var top = _state.Top;
        Utf8Marshaller.Push(_state, Payload);
        _ = Utf8Marshaller.TryRead(_state, -1, out var value);
        var length = value.Length;
        _state.SetTop(top);
        return length;
    }

    private byte[] Payload { get; set; } = [];
}
