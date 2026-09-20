using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using EngineAddressList = CheatEngine.SDK.Engine.AddressList.AddressList;

namespace CheatEngine.SDK.Engine.Tests.AddressList;

/// <summary>Fixture-backed contracts for CE 7.7 address-list names, nil handling, and stack restoration.</summary>
[Trait("Category", "NativeLua")]
public sealed class AddressListLuaTests
{
    [Fact]
    public void Current_address_list_and_created_records_are_borrowed_handles()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var listObject = FakeHost.CreateObject(L, "Probe", "o.props.Count = 2");
        var recordObject = FakeHost.CreateObject(L, "Probe", "o.props.ID = 81; o.props.Index = 0");
        EngineAddressList list = new(listObject);
        MemoryRecord expected = new(recordObject);

        SetGlobal(L, "list"u8, list);
        SetGlobal(L, "record"u8, expected);
        EngineTest.Run(L, """
                          function getAddressList() return list end
                          list.getMemoryRecord = function(index)
                            if index == 0 then return record end
                            return nil
                          end
                          list.getMemoryRecordByID = function(id)
                            if id == 81 then return record end
                            return nil
                          end
                          list.getSelectedRecord = function() return record end
                          list.createMemoryRecord = function() return record end
                          list.setSelectedRecord = function(value) list.SelectedRecordSet = value end
                          """u8);

        Assert.True(AddressListAccess.TryGetCurrent(out var current));
        Assert.Equal(list, current);
        Assert.True(current.TryGetCount(out var count));
        Assert.Equal(2, count);
        Assert.True(current.TryGetMemoryRecord(0, out var byIndex));
        Assert.Equal(expected, byIndex);
        Assert.False(current.TryGetMemoryRecord(1, out var missingByIndex));
        Assert.True(missingByIndex.IsNull);
        Assert.True(current.TryGetMemoryRecordById(new MemoryRecordId(81), out var byId));
        Assert.Equal(expected, byId);
        Assert.False(current.TryGetMemoryRecordById(new MemoryRecordId(99), out var missingById));
        Assert.True(missingById.IsNull);
        Assert.True(current.TryGetSelectedRecord(out var selected));
        Assert.Equal(expected, selected);
        Assert.True(current.TryCreateMemoryRecord(out var created));
        Assert.Equal(expected, created);
        Assert.True(current.TrySetSelectedRecord(expected));
        Assert.True(listObject.TryGetProperty<MemoryRecord, MemoryRecord>("SelectedRecordSet"u8, out var set));
        Assert.Equal(expected, set);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Memory_record_properties_methods_and_children_follow_the_documented_shapes()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var recordObject = FakeHost.CreateObject(L, "Probe",
            "o.props.ID = 81; o.props.Index = 3; o.props.Description = 'health'; o.props.Address = 'game+10'; o.props.Value = '100'; o.props.Type = 2");
        var parentObject = FakeHost.CreateObject(L, "Probe");
        var childObject = FakeHost.CreateObject(L, "Probe");
        MemoryRecord record = new(recordObject);
        MemoryRecord parent = new(parentObject);
        MemoryRecord child = new(childObject);

        SetGlobal(L, "record"u8, record);
        SetGlobal(L, "parent"u8, parent);
        SetGlobal(L, "child"u8, child);
        EngineTest.Run(L, """
                          record.Parent = parent
                          record[0] = child
                          record.getCurrentAddress = function() return -1 end
                          """u8);

        AssertRecordReads(record, parent, child);
        AssertRecordWrites(record, recordObject);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Negative_object_indices_throw_before_the_host_is_contacted()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineAddressList list = new(FakeHost.CreateObject(scope.State, "Probe"));
        MemoryRecord record = new(FakeHost.CreateObject(scope.State, "Probe"));
        var providerCalls = FakeHost.ProviderCalls;
        var pusherCalls = FakeHost.PusherCalls;

        Assert.Throws<ArgumentOutOfRangeException>(() => list.TryGetMemoryRecord(-1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.TryGetChild(-1, out _));
        Assert.Equal(providerCalls, FakeHost.ProviderCalls);
        Assert.Equal(pusherCalls, FakeHost.PusherCalls);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Protected_errors_and_wrong_results_return_false_and_restore_the_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var listObject = FakeHost.CreateObject(L, "Probe");
        EngineAddressList list = new(listObject);

        SetGlobal(L, "list"u8, list);
        EngineTest.Run(L, """
                          function getAddressList() error('no list') end
                          list.getMemoryRecord = function() return 42 end
                          """u8);

        Assert.False(AddressListAccess.TryGetCurrent(out var absent));
        Assert.True(absent.IsNull);
        Assert.False(list.TryGetMemoryRecord(0, out var wrong));
        Assert.True(wrong.IsNull);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Warm_address_list_lookup_and_record_access_allocate_nothing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineAddressList list = new(FakeHost.CreateObject(L, "Probe", "o.props.Count = 2"));
        MemoryRecord record = new(FakeHost.CreateObject(L, "Probe", "o.props.ID = 81"));
        long sink = 0;

        SetGlobal(L, "list"u8, list);
        SetGlobal(L, "record"u8, record);
        EngineTest.Run(L, """
                          function getAddressList() return list end
                          list.getMemoryRecord = function(index) return record end
                          """u8);

        Assert.True(AddressListAccess.TryGetCurrent(out _));
        AllocationGate.AssertZero(() =>
        {
            if (!AddressListAccess.TryGetCurrent(out var current)) Assert.Fail("getAddressList");
            if (!current.TryGetCount(out var count)) Assert.Fail("getCount");
            if (!current.TryGetMemoryRecord(0, out var found)) Assert.Fail("getMemoryRecord");
            if (!found.TryGetId(out var id)) Assert.Fail("ID");
            sink += count + id.Value;
        });

        Assert.NotEqual(0, sink);
        Assert.Equal(0, L.Top);
    }

    private static void SetGlobal<T>(LuaState state, ReadOnlySpan<byte> name, T value)
        where T : struct, ILuaMarshaller<T>
    {
        using LuaFrame frame = new(state);
        T.Push(state, value);
        var status = state.TrySetGlobal(name);
        if (!status.IsOk) Assert.Fail("Setting the test global failed: " + EngineTest.ErrorMessage(state, status));
    }

    private static void AssertRecordReads(MemoryRecord record, MemoryRecord parent, MemoryRecord child)
    {
        Assert.True(record.TryGetId(out var id));
        Assert.Equal(new MemoryRecordId(81), id);
        Assert.True(record.TryGetIndex(out var index));
        Assert.Equal(3, index);
        Assert.True(record.TryGetDescription(out var description));
        Assert.Equal("health", description);
        Assert.True(record.TryGetAddressExpression(out var expression));
        Assert.Equal("game+10", expression);
        Assert.True(record.TryGetValue(out var value));
        Assert.Equal("100", value);
        Assert.True(record.TryGetVariableType(out var variableType));
        Assert.Equal(VariableType.Dword, variableType);
        Assert.True(record.TryGetCurrentAddress(out var address));
        Assert.Equal(ulong.MaxValue, address.Value);
        Assert.True(record.TryGetParent(out var actualParent));
        Assert.Equal(parent, actualParent);
        Assert.True(record.TryGetChild(0, out var actualChild));
        Assert.Equal(child, actualChild);
        Assert.False(record.TryGetChild(1, out var missing));
        Assert.True(missing.IsNull);
    }

    private static void AssertRecordWrites(MemoryRecord record, CEObject recordObject)
    {
        Assert.True(record.TrySetDescription("mana"));
        Assert.True(record.TrySetAddressExpression("game+20"));
        Assert.True(record.TrySetValue("101"));
        Assert.True(record.TrySetVariableType(VariableType.Qword));
        Assert.True(recordObject.TryGetProperty<StringMarshaller, string>("Description"u8,
            out var changedDescription));
        Assert.Equal("mana", changedDescription);
        Assert.True(recordObject.TryGetProperty<StringMarshaller, string>("Address"u8,
            out var changedAddress));
        Assert.Equal("game+20", changedAddress);
        Assert.True(recordObject.TryGetProperty<StringMarshaller, string>("Value"u8,
            out var changedValue));
        Assert.Equal("101", changedValue);
        Assert.True(recordObject.TryGetProperty<EnumMarshaller<VariableType>, VariableType>(
            "Type"u8, out var changedType));
        Assert.Equal(VariableType.Qword, changedType);
    }
}
