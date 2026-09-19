using BenchmarkDotNet.Attributes;
using CESDK.Lua.Marshalling;
using CESDK.Lua.State;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Benchmarks;

/// <summary>
///     Push+read of each <c>CESDK.Lua</c> scalar marshaller, on a real Lua 5.3 state (categories <c>Transition</c> and
///     <c>Strings</c>): the marshaller round trip only. Every benchmark restores the stack top it started from, so the
///     state never grows across the run's millions of invocations. <c>MemoryDiagnoser</c> is the headline metric here:
///     every marshaller but <see cref="PushReadString" /> is documented allocation-free; the benchmark is the proof.
/// </summary>
[MemoryDiagnoser(false)]
[ShortRunJob]
[BenchmarkCategory("Transition", "Strings")]
public class MarshallerBenchmarks : IDisposable
{
    private LuaState _l;
    private NativeLuaState? _state;

    /// <inheritdoc />
    public void Dispose()
    {
        _state?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Opens a fresh, independent Lua state.</summary>
    [GlobalSetup]
    public void Setup()
    {
        NativeLuaLibrary.ThrowIfUnavailable();
        _state = new NativeLuaState();
        _l = new LuaState(_state.Pointer);
    }

    /// <summary>
    ///     Closes the state. BenchmarkDotNet does not call <see cref="Dispose" /> itself; this is what
    ///     <c>[GlobalCleanup]</c> is for.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>Push+read of <see cref="Int32Marshaller" />.</summary>
    [Benchmark]
    public int PushReadInt32()
    {
        var top = _l.Top;
        Int32Marshaller.Push(_l, 42);
        _ = Int32Marshaller.TryRead(_l, -1, out var value);
        _l.SetTop(top);
        return value;
    }

    /// <summary>Push+read of <see cref="Int64Marshaller" />.</summary>
    [Benchmark]
    public long PushReadInt64()
    {
        var top = _l.Top;
        Int64Marshaller.Push(_l, 42L);
        _ = Int64Marshaller.TryRead(_l, -1, out var value);
        _l.SetTop(top);
        return value;
    }

    /// <summary>Push+read of <see cref="SingleMarshaller" />.</summary>
    [Benchmark]
    public float PushReadSingle()
    {
        var top = _l.Top;
        SingleMarshaller.Push(_l, 4.2f);
        _ = SingleMarshaller.TryRead(_l, -1, out var value);
        _l.SetTop(top);
        return value;
    }

    /// <summary>Push+read of <see cref="DoubleMarshaller" />.</summary>
    [Benchmark]
    public double PushReadDouble()
    {
        var top = _l.Top;
        DoubleMarshaller.Push(_l, 4.2);
        _ = DoubleMarshaller.TryRead(_l, -1, out var value);
        _l.SetTop(top);
        return value;
    }

    /// <summary>Push+read of <see cref="BooleanMarshaller" />.</summary>
    [Benchmark]
    public bool PushReadBoolean()
    {
        var top = _l.Top;
        BooleanMarshaller.Push(_l, true);
        _ = BooleanMarshaller.TryRead(_l, -1, out var value);
        _l.SetTop(top);
        return value;
    }

    /// <summary>Push+read of <see cref="AddressMarshaller" /> (<see cref="nuint" />, numbers only).</summary>
    [Benchmark]
    public nuint PushReadAddress()
    {
        var top = _l.Top;
        AddressMarshaller.Push(_l, 0x00400000);
        _ = AddressMarshaller.TryRead(_l, -1, out var value);
        _l.SetTop(top);
        return value;
    }

    /// <summary>
    ///     Push+read of <see cref="Utf8Marshaller" /> (<see cref="ReadOnlySpan{T}" /> of UTF-8 bytes; allocation-free
    ///     both ways).
    /// </summary>
    [Benchmark]
    public int PushReadUtf8()
    {
        var top = _l.Top;
        Utf8Marshaller.Push(_l, "cesdk"u8);
        _ = Utf8Marshaller.TryRead(_l, -1, out var value);
        var length = value.Length;
        _l.SetTop(top);
        return length;
    }

    /// <summary>
    ///     Push+read of <see cref="StringMarshaller" /> (<see cref="string" />): the one marshaller documented to
    ///     allocate on read (a new managed string); the odd one out in the <c>MemoryDiagnoser</c> column on purpose.
    /// </summary>
    [Benchmark]
    public int PushReadString()
    {
        var top = _l.Top;
        StringMarshaller.Push(_l, "cesdk");
        _ = StringMarshaller.TryRead(_l, -1, out var value);
        _l.SetTop(top);
        return value?.Length ?? 0;
    }
}
