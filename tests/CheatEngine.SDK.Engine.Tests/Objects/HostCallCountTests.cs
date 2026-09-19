using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>
///     The host-side budget of every primitive, counted through the fake host's doubles: one state acquisition per typed
///     operation and one push of the object per operation, on the success and on the failure paths. The Lua C API
///     calls in between are not counted here.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class HostCallCountTests
{
    [Fact]
    public void Typed_members_acquire_the_state_once_and_push_the_object_once()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var probe = FakeHost.CreateObject(
            scope.State,
            "Probe",
            "o.props.Count = 3; o.getters.Bad = function() error('x') end; o.setters.Locked = function() error('y') end");

        AssertOneAcquisitionAndOnePush(() => probe.TryGetProperty<Int32Marshaller, int>("Count"u8, out _), "typed get");
        AssertOneAcquisitionAndOnePush(() => probe.TrySetProperty<Int32Marshaller, int>("Count"u8, 4), "typed set");
        AssertOneAcquisitionAndOnePush(
            () => probe.TrySetProperty<Utf8Marshaller, ReadOnlySpan<byte>>("Name"u8, "renamed"u8),
            "typed set of a string");
        AssertOneAcquisitionAndOnePush(() => probe.TryCallMethod("getClassName"u8), "typed call");
        AssertOneAcquisitionAndOnePush(
            () => probe.TryCallMethod<Int32Marshaller, int>("getCount"u8, out var count) && count == 4,
            "typed call with result");

        AssertOneAcquisitionAndOnePush(() => !probe.TryGetProperty<Int32Marshaller, int>("Bad"u8, out _),
            "raising get");
        AssertOneAcquisitionAndOnePush(() => !probe.TryGetProperty<Int32Marshaller, int>("Missing"u8, out _),
            "get of a missing member");
        AssertOneAcquisitionAndOnePush(() => !probe.TrySetProperty<Int32Marshaller, int>("Locked"u8, 1), "raising set");
        AssertOneAcquisitionAndOnePush(() => !probe.TryCallMethod("raise"u8), "raising call");
        AssertOneAcquisitionAndOnePush(() => !probe.TryCallMethod("notAMethod"u8),
            "call of a member that is not a function");
        AssertOneAcquisitionAndOnePush(() => !probe.TryCallMethod<Int32Marshaller, int>("getClassName"u8, out _),
            "call whose result has the wrong kind");
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Stack_level_members_push_the_object_once_and_never_acquire_a_state()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        using LuaFrame frame = new(L);
        var probe = FakeHost.CreateObject(L, "Probe", "o.props.Count = 3; o.items = { 'a' }");

        AssertNoAcquisitionAndOnePush(() => probe.TryGetProperty(L, "Count"u8).IsOk, "get");
        L.PushInteger(5);
        AssertNoAcquisitionAndOnePush(() => probe.TrySetProperty(L, "Count"u8).IsOk, "set");
        AssertNoAcquisitionAndOnePush(() => probe.TryGetIndex(L, 0).IsOk, "index get");
        L.PushString("b"u8);
        AssertNoAcquisitionAndOnePush(() => probe.TrySetIndex(L, 1).IsOk, "index set");
        AssertNoAcquisitionAndOnePush(() => probe.TryPushMethod(L, "getCount"u8).IsOk, "method push");
        L.PushInteger(1);
        L.PushInteger(2);
        AssertNoAcquisitionAndOnePush(() => probe.TryCallMethod(L, "add"u8, 2, 1).IsOk, "call with arguments");
        AssertNoAcquisitionAndOnePush(() => !probe.TryCallMethod(L, "missing"u8, 0, 0).IsOk,
            "call of a missing method");
        AssertNoAcquisitionAndOnePush(() => probe.TryGetPropertyLeavingObject(L, "Count"u8).IsOk,
            "get leaving the object");
        AssertNoAcquisitionAndOnePush(() => probe.TryPushMethodLeavingObject(L, "getCount"u8).IsOk,
            "method push leaving the object");
    }

    private static void AssertOneAcquisitionAndOnePush(Func<bool> operation, string what)
    {
        var providerCalls = FakeHost.ProviderCalls;
        var pusherCalls = FakeHost.PusherCalls;

        Assert.True(operation(), what + " did not produce the expected outcome");

        Assert.True(FakeHost.ProviderCalls - providerCalls == 1,
            what + " acquired the state " + (FakeHost.ProviderCalls - providerCalls) + " times, not once");
        Assert.True(FakeHost.PusherCalls - pusherCalls == 1,
            what + " pushed the object " + (FakeHost.PusherCalls - pusherCalls) + " times, not once");
    }

    private static void AssertNoAcquisitionAndOnePush(Func<bool> operation, string what)
    {
        var providerCalls = FakeHost.ProviderCalls;
        var pusherCalls = FakeHost.PusherCalls;

        Assert.True(operation(), what + " did not produce the expected outcome");

        Assert.True(FakeHost.ProviderCalls == providerCalls,
            what + " acquired a state; a stack-level member works on the state it was given");
        Assert.True(FakeHost.PusherCalls - pusherCalls == 1,
            what + " pushed the object " + (FakeHost.PusherCalls - pusherCalls) + " times, not once");
    }
}
