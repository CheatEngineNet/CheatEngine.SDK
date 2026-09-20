using BenchmarkDotNet.Attributes;
using CheatEngine.SDK.Benchmarks.Support;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Benchmarks;

/// <summary>
///     Measures target- and host-memory scalar operations against the pinned Lua fixture. The fixture implements the
///     exact Lua global call shapes, cache use, address marshalling, protected calls and result marshalling, but it does
///     not claim to measure Cheat Engine's OS process-memory implementation.
/// </summary>
/// <remarks>
///     The representative cases are signed 32- and 64-bit read/write calls for both address spaces. The separate
///     byte, string, floating-point and pointer scenarios are deliberately deferred in <c>BaselineMetadata.md</c> so a
///     benchmark is added with its own real caller shape rather than as an indiscriminate Cartesian product.
/// </remarks>
[MemoryDiagnoser(false)]
[BenchmarkCategory("EngineApi", "TargetMemory", "Fixture")]
public class MemoryScalarBenchmarks : IDisposable
{
    private static readonly Address Address32 = Address.FromUInt64(0x0000_0000_00CE_7700);

    private static readonly Address Address64 = Address.FromUInt64(0x0000_0001_00CE_7700);

    private static readonly HostAddress HostAddress32 = new((nuint)0x0000_0000_00CE_7700);

    // The plugin host is x64, but this project still compiles its source without an x64-only constant evaluator.
    private static readonly HostAddress HostAddress64 = new(unchecked((nuint)0x0000_0001_00CE_7700UL));

    private NativeLuaState? _state;

    /// <inheritdoc />
    public void Dispose()
    {
        LuaRuntime.Detach();
        _state?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Opens a fixture state with stand-ins for CE 7.7's target and local scalar globals and warms every global
    ///     cache used below.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        NativeLuaLibrary.ThrowIfUnavailable();
        _state = new NativeLuaState();
        var state = FakeHostRuntime.Attach(_state, false);
        var defined = state.TryExecute(ScalarStandIns, 0);
        if (!defined.IsOk)
            throw new InvalidOperationException("Defining scalar-memory fixture globals failed: " +
                                                LuaError.FromStack(state, defined));

        if (!TargetMemory.TryWriteInt32(Address32, -42, out _))
            throw new InvalidOperationException("Warming writeInteger failed.");

        if (!TargetMemory.TryWriteInt64(Address64, 0x1_0000_0000L, out _))
            throw new InvalidOperationException("Warming writeQword failed.");

        if (!HostMemory.TryWriteInt32(HostAddress32, -42, out _) ||
            !HostMemory.TryWriteInt64(HostAddress64, 0x1_0000_0000L, out _))
            throw new InvalidOperationException("Warming local scalar writes failed.");

        if (!TargetMemory.TryReadInt32(Address32, out _, out _) ||
            !TargetMemory.TryReadInt64(Address64, out _, out _) ||
            !HostMemory.TryReadInt32(HostAddress32, out _, out _) ||
            !HostMemory.TryReadInt64(HostAddress64, out _, out _))
            throw new InvalidOperationException("Warming scalar reads failed.");
    }

    /// <summary>
    ///     Detaches the ambient runtime and closes the fixture state. BenchmarkDotNet invokes this, not
    ///     <see cref="Dispose" />, after the measurements.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>Reads a signed 32-bit target scalar through <see cref="TargetMemory.TryReadInt32" />.</summary>
    [Benchmark(Baseline = true)]
    public int TargetReadInt32()
    {
        if (!TargetMemory.TryReadInt32(Address32, out var value, out _))
            throw new InvalidOperationException("Fixture readInteger unexpectedly returned nil.");

        return value;
    }

    /// <summary>Writes a signed 32-bit target scalar through <see cref="TargetMemory.TryWriteInt32" />.</summary>
    [Benchmark]
    public bool TargetWriteInt32()
    {
        return TargetMemory.TryWriteInt32(Address32, -42, out _);
    }

    /// <summary>Reads a signed 64-bit target scalar through <see cref="TargetMemory.TryReadInt64" />.</summary>
    [Benchmark]
    public long TargetReadInt64()
    {
        if (!TargetMemory.TryReadInt64(Address64, out var value, out _))
            throw new InvalidOperationException("Fixture readQword unexpectedly returned nil.");

        return value;
    }

    /// <summary>Writes a signed 64-bit target scalar through <see cref="TargetMemory.TryWriteInt64" />.</summary>
    [Benchmark]
    public bool TargetWriteInt64()
    {
        return TargetMemory.TryWriteInt64(Address64, 0x1_0000_0000L, out _);
    }

    /// <summary>Reads a signed 32-bit Cheat Engine host scalar through <see cref="HostMemory.TryReadInt32" />.</summary>
    [Benchmark]
    public int HostReadInt32()
    {
        if (!HostMemory.TryReadInt32(HostAddress32, out var value, out _))
            throw new InvalidOperationException("Fixture readIntegerLocal unexpectedly returned nil.");

        return value;
    }

    /// <summary>Writes a signed 32-bit Cheat Engine host scalar through <see cref="HostMemory.TryWriteInt32" />.</summary>
    [Benchmark]
    public bool HostWriteInt32()
    {
        return HostMemory.TryWriteInt32(HostAddress32, -42, out _);
    }

    /// <summary>Reads a signed 64-bit Cheat Engine host scalar through <see cref="HostMemory.TryReadInt64" />.</summary>
    [Benchmark]
    public long HostReadInt64()
    {
        if (!HostMemory.TryReadInt64(HostAddress64, out var value, out _))
            throw new InvalidOperationException("Fixture readQwordLocal unexpectedly returned nil.");

        return value;
    }

    /// <summary>Writes a signed 64-bit Cheat Engine host scalar through <see cref="HostMemory.TryWriteInt64" />.</summary>
    [Benchmark]
    public bool HostWriteInt64()
    {
        return HostMemory.TryWriteInt64(HostAddress64, 0x1_0000_0000L, out _);
    }

    // Stand-ins intentionally mirror the CE Lua return shapes only. They have no authorization to read another process.
    private static ReadOnlySpan<byte> ScalarStandIns => """
                                                     local mem32 = {}
                                                     local mem64 = {}
                                                     function readInteger(address, signed)
                                                         local value = mem32[address]
                                                         if value == nil or signed then return value end
                                                         return value < 0 and value + 4294967296 or value
                                                     end
                                                     function writeInteger(address, value) mem32[address] = value; return true end
                                                     function readQword(address) return mem64[address] end
                                                     function writeQword(address, value) mem64[address] = value; return true end
                                                     local host32 = {}
                                                     local host64 = {}
                                                     function readIntegerLocal(address, signed)
                                                         local value = host32[address]
                                                         if value == nil or signed then return value end
                                                         return value < 0 and value + 4294967296 or value
                                                     end
                                                     function writeIntegerLocal(address, value) host32[address] = value; return true end
                                                     function readQwordLocal(address) return host64[address] end
                                                     function writeQwordLocal(address, value) host64[address] = value; return true end
                                                     """u8;
}
