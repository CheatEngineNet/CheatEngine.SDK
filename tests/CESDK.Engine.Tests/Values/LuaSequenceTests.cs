using CESDK.Engine.Tests.Support;
using CESDK.Engine.Values;
using CESDK.Lua.Calls;
using CESDK.Lua.State;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Engine.Tests.Values;

/// <summary>Zero-based access to Lua sequences: the conversion happens inside the extensions, never at a call site.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaSequenceTests
{
    [Fact]
    public void Protected_access_reads_and_writes_elements_by_zero_based_index()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = EngineTest.View(state);
        using LuaFrame frame = new(L);

        EngineTest.Run(L, "return { 10, 20, 30 }"u8, 1);
        var table = L.Top;

        Assert.True(L.TryGetSequenceItem(table, 0).IsOk);
        Assert.Equal(10, EngineTest.ReadInteger(L, -1));
        Assert.True(L.TryGetSequenceItem(-2, 2).IsOk);
        Assert.Equal(30, EngineTest.ReadInteger(L, -1));
        Assert.True(L.TryGetSequenceItem(table, 3).IsOk);
        Assert.True(L.IsNil(-1));
        L.SetTop(table);

        L.PushInteger(25);
        Assert.True(L.TrySetSequenceItem(table, 1).IsOk);
        Assert.Equal(table, L.Top);
        Assert.Equal(LuaType.Number, L.RawGetIndex(table, 2));
        Assert.Equal(25, EngineTest.ReadInteger(L, -1));
    }

    [Fact]
    public void Raw_access_reads_writes_and_counts_by_zero_based_index()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = EngineTest.View(state);
        using LuaFrame frame = new(L);

        L.CreateTable(3);
        var table = L.Top;
        L.PushInteger(100);
        L.RawSetSequenceItem(table, 0);
        L.PushInteger(200);
        L.RawSetSequenceItem(table, 1);
        L.PushInteger(300);
        L.RawSetSequenceItem(table, 2);

        Assert.Equal(3, L.RawSequenceCount(table));
        Assert.Equal(LuaType.Number, L.RawGetSequenceItem(table, 2));
        Assert.Equal(300, EngineTest.ReadInteger(L, -1));
        Assert.Equal(LuaType.Nil, L.RawGetSequenceItem(table, 3));
        Assert.Equal(LuaType.Number, L.RawGetIndex(table, 1));
        Assert.Equal(100, EngineTest.ReadInteger(L, -1));
        Assert.Equal(LuaType.Nil, L.RawGetIndex(table, 0));
    }

    [Fact]
    public void A_negative_index_is_refused_before_anything_is_pushed()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = EngineTest.View(state);
        L.CreateTable();
        var top = L.Top;

        Assert.Throws<ArgumentOutOfRangeException>(() => L.TryGetSequenceItem(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => L.RawGetSequenceItem(1, -1));
        L.PushInteger(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => L.TrySetSequenceItem(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => L.RawSetSequenceItem(1, -1));
        Assert.Equal(top + 1, L.Top);
    }

    [Fact]
    public void A_raising_index_metamethod_is_a_status_not_a_crash()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = EngineTest.View(state);
        using LuaFrame frame = new(L);

        EngineTest.Run(L, "return setmetatable({}, { __index = function(t, k) error('no element ' .. k) end })"u8, 1);
        var status = L.TryGetSequenceItem(-1, 4);
        Assert.Equal(LuaStatus.RuntimeError, status);
        Assert.Contains("no element 5", EngineTest.ErrorMessage(L, status), StringComparison.Ordinal);
    }
}
