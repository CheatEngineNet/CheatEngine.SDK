using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>AOBScan's exact CE argument positions, protected failure conversion, and owned StringList result.</summary>
[Trait("Category", "NativeLua")]
public sealed class AobScannerTests
{
    [Fact]
    public void TryScan_default_options_passes_only_the_pattern_and_returns_a_caller_owned_list()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = AobStringListTestHost.CreateList(L);
        AobStringListTestHost.InstallAobScan(L, handle);

        Assert.True(AobScanner.TryScan("48 8B ?? 89", out var results));
        Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
        Assert.True(owned.Value.TryGetCount(out var count));
        Assert.Equal(2, count);
        Assert.True(owned.Value.TryGetItem(1, out var second));
        Assert.Equal("7FF6A1B2C3D4", second);
        Assert.Equal(0, L.Top);

        EngineTest.Run(L, "return aob_argument_count, aob_pattern, aob_protection"u8, 3);
        Assert.Equal(1, EngineTest.ReadInteger(L, -3));
        Assert.Equal("48 8B ?? 89", EngineTest.ReadString(L, -2));
        Assert.True(L.IsNil(-1));
        L.SetTop(0);

        owned.Dispose();
        Assert.True(owned.IsDisposed);
        Assert.True(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void TryScan_alignment_preserves_a_nil_protection_slot_and_all_four_CE_positions()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = AobStringListTestHost.CreateList(L);
        AobStringListTestHost.InstallAobScan(L, handle);
        AobScanOptions options = new(null, FastScanMethod.Aligned, "16");

        Assert.True(AobScanner.TryScan("90 90", options, out var results));
        Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
        EngineTest.Run(L, "return aob_argument_count, aob_protection, aob_alignment, aob_alignment_parameter"u8, 4);
        Assert.Equal(4, EngineTest.ReadInteger(L, -4));
        Assert.True(L.IsNil(-3));
        Assert.Equal((long)FastScanMethod.Aligned, EngineTest.ReadInteger(L, -2));
        Assert.Equal("16", EngineTest.ReadString(L, -1));
        L.SetTop(0);

        owned.Dispose();
        Assert.True(FakeHost.IsDestroyed(L, handle));
    }

    [Fact]
    public void TryScan_protection_only_passes_two_CE_positions_without_synthesizing_alignment_values()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = AobStringListTestHost.CreateList(L);
        AobStringListTestHost.InstallAobScan(L, handle);
        AobScanOptions options = new("+X-C-W", FastScanMethod.NotAligned, null);

        Assert.True(AobScanner.TryScan("CC", options, out var results));
        Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
        EngineTest.Run(L, "return aob_argument_count, aob_protection, aob_alignment"u8, 3);
        Assert.Equal(2, EngineTest.ReadInteger(L, -3));
        Assert.Equal("+X-C-W", EngineTest.ReadString(L, -2));
        Assert.True(L.IsNil(-1));
        L.SetTop(0);

        owned.Dispose();
        Assert.True(FakeHost.IsDestroyed(L, handle));
    }

    [Fact]
    public void TryScan_nil_or_raising_result_returns_false_and_restores_the_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));

        Assert.False(AobScanner.TryScan("nil-result", out var nilResults));
        Assert.Null(nilResults);
        Assert.Equal(0, L.Top);

        Assert.False(AobScanner.TryScan("raise", out var raisedResults));
        Assert.Null(raisedResults);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void TryScan_while_detached_throws_without_attempting_lua_access()
    {
        LuaRuntime.Detach();

        Assert.Throws<InvalidOperationException>(() => AobScanner.TryScan("90", out _));
    }
}
