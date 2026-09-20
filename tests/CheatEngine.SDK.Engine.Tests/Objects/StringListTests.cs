using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Scanning.Aob;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>Borrowed StringList handles, CE StringList/Strings members, and explicit factory ownership.</summary>
[Trait("Category", "NativeLua")]
public sealed class StringListTests
{
    [Fact]
    public void Handle_properties_and_zero_based_indexer_round_trip_without_stack_residue()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = AobStringListTestHost.CreateList(L);
        var list = StringList.FromHandle(handle);

        Assert.Equal(list, StringList.FromHandle(handle));
        Assert.False(list.IsNull);
        Assert.True(list.TryGetCount(out var count));
        Assert.Equal(2, count);
        Assert.True(list.TryGetItem(0, out var first));
        Assert.Equal("00401000", first);
        Assert.True(list.TrySetItem(1, "00402000"));
        Assert.True(list.TryGetItem(1, out var replacement));
        Assert.Equal("00402000", replacement);

        Assert.True(list.TryGetSorted(out var sorted));
        Assert.False(sorted);
        Assert.True(list.TrySetSorted(value: true));
        Assert.True(list.TryGetSorted(out sorted));
        Assert.True(sorted);
        Assert.True(list.TryGetCaseSensitive(out var caseSensitive));
        Assert.True(caseSensitive);
        Assert.True(list.TrySetCaseSensitive(value: false));
        Assert.True(list.TryGetCaseSensitive(out caseSensitive));
        Assert.False(caseSensitive);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Strings_methods_preserve_CE_zero_based_indices_and_update_the_list()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var list = StringList.FromHandle(AobStringListTestHost.CreateList(L));

        Assert.True(list.TryAdd("00403000", out var addedIndex));
        Assert.Equal(2, addedIndex);
        Assert.True(list.TryIndexOf("00403000", out var foundIndex));
        Assert.Equal(2, foundIndex);
        Assert.True(list.TryIndexOf("missing", out var missingIndex));
        Assert.Equal(-1, missingIndex);
        Assert.True(list.TryGetText(out var text));
        Assert.Equal("00401000\n7FF6A1B2C3D4\n00403000", text);

        Assert.True(list.TryDelete(1));
        Assert.True(list.TryGetCount(out var count));
        Assert.Equal(2, count);
        Assert.True(list.TrySetText("only line"));
        Assert.True(list.TryGetItem(0, out var onlyLine));
        Assert.Equal("only line", onlyLine);
        Assert.True(list.TryClear());
        Assert.True(list.TryGetCount(out count));
        Assert.Equal(0, count);
        Assert.Equal(0, L.Top);
    }

    [Theory]
    [InlineData(DuplicateHandling.Ignore)]
    [InlineData(DuplicateHandling.Accept)]
    [InlineData(DuplicateHandling.Error)]
    public void Duplicates_property_round_trips_each_numeric_CE_enum_value(DuplicateHandling expected)
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var list = StringList.FromHandle(AobStringListTestHost.CreateList(L));

        Assert.True(list.TryGetDuplicates(out var duplicates));
        Assert.Equal(DuplicateHandling.Accept, duplicates);
        Assert.True(list.TrySetDuplicates(expected));
        Assert.True(list.TryGetDuplicates(out duplicates));
        Assert.Equal(expected, duplicates);
        Assert.Equal(0, L.Top);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void TrySetDuplicates_value_is_not_defined_throws_before_lua(int rawValue)
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var list = StringList.FromHandle(AobStringListTestHost.CreateList(L));
        var providerCalls = FakeHost.ProviderCalls;
        var pusherCalls = FakeHost.PusherCalls;

        Assert.Throws<ArgumentOutOfRangeException>(() => list.TrySetDuplicates((DuplicateHandling)rawValue));

        Assert.Equal(providerCalls, FakeHost.ProviderCalls);
        Assert.Equal(pusherCalls, FakeHost.PusherCalls);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Negative_indices_are_rejected_before_touching_the_lua_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var list = StringList.FromHandle(AobStringListTestHost.CreateList(L));

        Assert.Throws<ArgumentOutOfRangeException>(() => list.TryGetItem(-1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.TrySetItem(-1, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.TryDelete(-1));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Factory_returns_an_owned_list_that_destroys_deterministically()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = AobStringListTestHost.CreateList(L);
        AobStringListTestHost.InstallStringListFactory(L, handle);

        Assert.True(StringLists.TryCreate(out var created));
        var owned = Assert.IsType<Owned<StringList>>(created);
        Assert.Equal(handle, owned.Handle);
        owned.Dispose();

        Assert.True(owned.IsDisposed);
        Assert.True(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Factory_and_handle_methods_throw_while_the_runtime_is_detached()
    {
        LuaRuntime.Detach();
        StringList list = new(new CEObject(0x1234));

        Assert.Throws<InvalidOperationException>(() => StringLists.TryCreate(out _));
        Assert.Throws<InvalidOperationException>(() => list.TryGetCount(out _));
    }
}
