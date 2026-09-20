using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     The Cheat Engine side of the bootstrap: a host-owned init record buffer (36 bytes, byte-packed, at an aligned or
///     an odd address) followed by guard bytes, the bootstrap call, and the three lifecycle calls made exactly as the
///     host makes them, through the function pointers read back from the record.
/// </summary>
/// <remarks>
///     <see cref="Dispose" /> ends the simulated host process: the host is returned to its never-bootstrapped state
///     (which detaches the runtime and releases every Lua callback the plugin forgot, through the state provider), the
///     provider is cleared and the buffer freed. Declare the simulator <i>after</i> the <c>NativeLuaState</c> it hands
///     out, so that the C# <see langword="using" /> order disposes it first, while that state is still open: the release
///     runs Lua
///     calls on it. Idempotent.
/// </remarks>
internal sealed unsafe class HostSimulator : IDisposable
{
    public const byte GuardByte = 0xCD;
    public const int GuardLength = 64;

    private readonly int _offset;
    private byte* _block;

    public HostSimulator(bool oddAddress = false)
    {
        _offset = oddAddress ? 1 : 0;
        var length = _offset + RecordSize + GuardLength;
        _block = (byte*)NativeMemory.Alloc((nuint)length);
        new Span<byte>(_block, length).Fill(GuardByte);
    }

    public static int RecordSize => sizeof(PluginInitRecord);

    /// <summary>The address the host passes as <c>args</c>.</summary>
    public nint RecordAddress => (nint)(_block + _offset);

    /// <summary>The 36 bytes of the record as the host sees them.</summary>
    public ReadOnlySpan<byte> RecordBytes => new(_block + _offset, RecordSize);

    /// <summary>True while no byte after the record has been touched.</summary>
    public bool GuardIntact =>
        new ReadOnlySpan<byte>(_block + _offset + RecordSize, GuardLength).IndexOfAnyExcept(GuardByte) < 0;

    /// <summary>True while the record still holds the fill pattern, i.e. nothing was written.</summary>
    public bool RecordUntouched => RecordBytes.IndexOfAnyExcept(GuardByte) < 0;

    public ref PluginInitRecord Record => ref *(PluginInitRecord*)RecordAddress;

    /// <summary>
    ///     Ends the simulated host: resets the host state while the fixture state is still open (see the type remarks),
    ///     clears the provider, frees the buffer.
    /// </summary>
    public void Dispose()
    {
        if (_block is null) return;

        PluginHost.ResetForTests();
        LuaRuntime.Detach();
        FakeExports.UseState(null);
        NativeMemory.Free(_block);
        _block = null;
    }

    /// <summary>The bootstrap call, as the generated entry point makes it.</summary>
    public int Initialize<TFactory>(int hostArgument = 0)
        where TFactory : IPluginFactory
    {
        return PluginHost.InitializeManaged<TFactory>(RecordAddress, hostArgument);
    }

    /// <summary>The host's version query through the record's pointer.</summary>
    public Bool32 CallGetVersion(PluginVersion* version, int size)
    {
        return Record.GetVersion(version, size);
    }

    /// <summary>The host's enable call through the record's pointer.</summary>
    public Bool32 CallEnable(ManagedExportedFunctions* exports, uint pluginId)
    {
        return Record.EnablePlugin(exports, pluginId);
    }

    /// <summary>The host's disable call through the record's pointer.</summary>
    public Bool32 CallDisable()
    {
        return Record.DisablePlugin();
    }
}
