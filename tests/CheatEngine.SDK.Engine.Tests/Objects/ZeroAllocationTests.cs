using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>The merge gate for this layer: object access allocates nothing on the managed side once warm.</summary>
[Trait("Category", "NativeLua")]
public sealed class ZeroAllocationTests
{
    [Fact]
    public void A_typed_property_get_allocates_nothing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var probe = FakeHost.CreateObject(scope.State, "Probe", "o.props.Count = 3; o.props.Result = '00400000'");
        long sink = 0;

        AllocationGate.AssertZero(() =>
        {
            if (!probe.TryGetProperty<Int32Marshaller, int>("Count"u8, out var count) || count != 3)
                Assert.Fail("Count");
            if (!probe.TryGetProperty<Address, Address>("Result"u8, out var result) || result.Value != 0x400000)
                Assert.Fail("Result");
            sink += count + (long)result.Value;
        });

        Assert.NotEqual(0, sink);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void A_typed_property_set_allocates_nothing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var probe = FakeHost.CreateObject(scope.State, "Probe");
        var i = 0;

        AllocationGate.AssertZero(() =>
        {
            if (!probe.TrySetProperty<Int32Marshaller, int>("Count"u8, ++i)) Assert.Fail("Count");
            if (!probe.TrySetProperty<Utf8Marshaller, ReadOnlySpan<byte>>("Name"u8, "renamed"u8)) Assert.Fail("Name");
        });

        Assert.True(probe.TryGetProperty<Int32Marshaller, int>("Count"u8, out var count));
        Assert.Equal(i, count);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void A_typed_method_call_allocates_nothing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var probe = FakeHost.CreateObject(scope.State, "Probe", "o.props.Count = 6");
        long sink = 0;

        AllocationGate.AssertZero(() =>
        {
            if (!probe.TryCallMethod<Int32Marshaller, int>("getCount"u8, out var count) || count != 6)
                Assert.Fail("getCount");
            if (!probe.TryCallMethod("getClassName"u8)) Assert.Fail("getClassName");
            sink += count;
        });

        Assert.NotEqual(0, sink);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void The_stack_level_primitives_allocate_nothing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var probe = FakeHost.CreateObject(L, "Probe", "o.props.Count = 3; o.items = { 'a' }");
        long sink = 0;

        AllocationGate.AssertZero(() =>
        {
            using LuaFrame frame = new(L);
            if (!probe.TryGetProperty(L, "Count"u8).IsOk) Assert.Fail("get");
            if (!L.TryReadInteger(-1, out var count)) Assert.Fail("read count");
            L.PushInteger(count + 1);
            if (!probe.TrySetProperty(L, "Count"u8).IsOk) Assert.Fail("set");
            if (!probe.TryGetIndex(L, 0).IsOk) Assert.Fail("index");
            L.PushInteger(1);
            L.PushInteger(2);
            if (!probe.TryCallMethod(L, "add"u8, 2, 1).IsOk) Assert.Fail("call");
            if (!L.TryReadInteger(-1, out var sum)) Assert.Fail("read sum");
            probe.Push(L);
            if (!CEObject.TryRead(L, -1, out var back) || back != probe) Assert.Fail("read");
            sink += count + sum;
        });

        Assert.NotEqual(0, sink);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void A_failing_access_allocates_nothing_either()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var probe = FakeHost.CreateObject(scope.State, "Probe", "o.getters.Bad = function() error('x') end");

        AllocationGate.AssertZero(() =>
        {
            if (probe.TryGetProperty<Int32Marshaller, int>("Bad"u8, out _)) Assert.Fail("Bad");
            if (probe.TryCallMethod("raise"u8)) Assert.Fail("raise");
            if (probe.TryCallMethod("notAMethod"u8)) Assert.Fail("notAMethod");
        });

        Assert.Equal(0, scope.State.Top);
    }
}
