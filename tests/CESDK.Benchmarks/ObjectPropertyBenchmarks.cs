using BenchmarkDotNet.Attributes;
using CESDK.Benchmarks.Support;
using CESDK.Engine.Objects;
using CESDK.Lua.Marshalling;
using CESDK.Lua.Runtime;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Benchmarks;

/// <summary>
///     A property get on a fake host object, through <c>CESDK.Engine.Objects.CEObject</c>'s typed primitive (category
///     <c>ObjectAccess</c>, read side only). <c>CESDK.Engine</c>'s <c>Objects/</c> primitives are usable standalone
///     against any binding that supplies a state provider and a host-object pusher (<see cref="FakeHostRuntime" />
///     stands in for Cheat Engine's <c>LuaPushClassInstance</c>), which is what lets this project benchmark them.
/// </summary>
[MemoryDiagnoser(false)]
[ShortRunJob]
[BenchmarkCategory("ObjectAccess")]
public class ObjectPropertyBenchmarks : IDisposable
{
    private CEObject _object;
    private NativeLuaState? _state;

    /// <inheritdoc />
    public void Dispose()
    {
        LuaRuntime.Detach();
        _state?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Opens a state, attaches the ambient runtime with a pusher and a one-property fake object model.</summary>
    [GlobalSetup]
    public void Setup()
    {
        NativeLuaLibrary.ThrowIfUnavailable();
        _state = new NativeLuaState();
        _ = FakeHostRuntime.Attach(_state, true);
        _object = new CEObject(0x0010_0000);
    }

    /// <summary>
    ///     Detaches the ambient runtime and closes the state. BenchmarkDotNet does not call <see cref="Dispose" />
    ///     itself; this is what <c>[GlobalCleanup]</c> is for.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>
    ///     <see cref="CEObject.TryGetProperty{TMarshaller, TValue}(ReadOnlySpan{byte}, out TValue)" />: provider, push
    ///     the object, protected <c>__index</c>, read, <c>settop</c> (nine transitions with a one-call marshaller).
    /// </summary>
    [Benchmark]
    public int PropertyGet()
    {
        _ = _object.TryGetProperty<Int32Marshaller, int>("Count"u8, out var value);
        return value;
    }
}
