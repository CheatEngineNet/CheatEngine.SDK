using System;
using System.Text;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using EngineAddressList = CheatEngine.SDK.Engine.AddressList.AddressList;

namespace CheatEngine.SDK.Engine.Tests.AddressList;

/// <summary>Native-Lua contracts for ID-addressed address-list mutation commands.</summary>
[Trait("Category", "NativeLua")]
public sealed class AddressListMutationsTests
{
    [Fact]
    public void Default_outcome_is_not_classified_as_a_completed_command()
    {
        MemoryRecordMutationOutcome outcome = default;

        Assert.Equal(MemoryRecordMutationEffect.NotAttempted, outcome.Effect);
        Assert.Equal(MemoryRecordMutationProblem.Uninitialized, outcome.Problem);
        Assert.False(outcome.IsCompleted);
    }

    [Fact]
    public void Delete_reports_each_address_list_preflight_failure_without_starting_a_mutation()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var top = L.Top;

        var missingGlobal = AddressListMutations.Delete(new MemoryRecordId(1));

        Assert.Equal(MemoryRecordMutationEffect.NotAttempted, missingGlobal.Effect);
        Assert.Equal(MemoryRecordMutationProblem.GlobalUnavailable, missingGlobal.Problem);
        Assert.Equal(top, L.Top);

        AssertDeleteAddressListPreflight("getAddressList = function() error('address list lookup failed') end",
            MemoryRecordMutationProblem.LuaFailure);
        AssertDeleteAddressListPreflight("getAddressList = function() return nil end",
            MemoryRecordMutationProblem.AddressListUnavailable);
        AssertDeleteAddressListPreflight("getAddressList = function() return 42 end",
            MemoryRecordMutationProblem.InvalidResult);
    }

    [Fact]
    public void Delete_distinguishes_missing_record_from_success_and_restores_stack()
    {
        using MutationFixture fixture = new();
        var top = fixture.State.Top;

        var missing = AddressListMutations.Delete(new MemoryRecordId(99));

        Assert.Equal(MemoryRecordMutationEffect.NotAttempted, missing.Effect);
        Assert.Equal(MemoryRecordMutationProblem.RecordNotFound, missing.Problem);
        Assert.Equal(0, FakeHost.DestroyedCount(fixture.State));
        Assert.Equal(top, fixture.State.Top);

        var success = AddressListMutations.Delete(new MemoryRecordId(1));

        Assert.True(success.IsCompleted);
        Assert.Equal(MemoryRecordMutationProblem.None, success.Problem);
        Assert.Equal(1, FakeHost.DestroyedCount(fixture.State));
        Assert.Equal(top, fixture.State.Top);
    }

    [Fact]
    public void Delete_preserves_a_non_callable_record_lookup_failure()
    {
        using MutationFixture fixture = new();
        var top = fixture.State.Top;
        fixture.Execute("list.getMemoryRecordByID = 42");

        var result = AddressListMutations.Delete(new MemoryRecordId(1));

        Assert.Equal(MemoryRecordMutationEffect.NotAttempted, result.Effect);
        Assert.Equal(MemoryRecordMutationProblem.LuaFailure, result.Problem);
        Assert.False(result.LuaStatus.IsOk);
        Assert.Equal(0, FakeHost.DestroyedCount(fixture.State));
        Assert.Equal(top, fixture.State.Top);
    }

    [Fact]
    public void Set_parent_rejects_self_missing_and_cyclic_hierarchies_before_assignment()
    {
        using MutationFixture fixture = new();
        var top = fixture.State.Top;

        var self = AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(1));
        var missing = AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(99));
        fixture.Execute("parent.Parent = child");
        var cycle = AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(2));

        Assert.Equal(MemoryRecordMutationProblem.SelfParent, self.Problem);
        Assert.Equal(MemoryRecordMutationProblem.ParentNotFound, missing.Problem);
        Assert.Equal(MemoryRecordMutationProblem.CycleDetected, cycle.Problem);
        Assert.All(new[] { self, missing, cycle }, outcome =>
            Assert.Equal(MemoryRecordMutationEffect.NotAttempted, outcome.Effect));
        fixture.Execute("assert(assigned == 0)");
        Assert.Equal(top, fixture.State.Top);
    }

    [Fact]
    public void Set_parent_obeys_an_explicit_parent_walk_bound()
    {
        using MutationFixture fixture = new();
        fixture.Execute("parent.Parent = grandparent");

        var result = AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(2),
            new MemoryRecordParentTraversalLimit(1));

        Assert.Equal(MemoryRecordMutationEffect.NotAttempted, result.Effect);
        Assert.Equal(MemoryRecordMutationProblem.TraversalLimitReached, result.Problem);
        fixture.Execute("assert(assigned == 0)");
    }

    [Fact]
    public void Set_parent_assigns_a_typed_parent_or_lua_nil_for_root()
    {
        using MutationFixture fixture = new();
        var top = fixture.State.Top;

        var parent = AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(2));
        fixture.Execute("assert(assigned == 1)");
        var root = AddressListMutations.SetParent(new MemoryRecordId(1), parentId: null);

        Assert.True(parent.IsCompleted);
        Assert.True(root.IsCompleted);
        fixture.Execute("assert(assigned == 2)");
        Assert.Equal(top, fixture.State.Top);
    }

    [Fact]
    public void Started_destroy_or_parent_assignment_error_is_indeterminate_and_not_retried()
    {
        using MutationFixture failingDestroy = new(destroyFails: true);
        var destroy = AddressListMutations.Delete(new MemoryRecordId(1));

        Assert.Equal(MemoryRecordMutationEffect.Indeterminate, destroy.Effect);
        Assert.Equal(MemoryRecordMutationProblem.LuaFailure, destroy.Problem);
        Assert.Equal(0, FakeHost.DestroyedCount(failingDestroy.State));

        using MutationFixture failingSetter = new(parentSetterFails: true);
        var assignment = AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(2));

        Assert.Equal(MemoryRecordMutationEffect.Indeterminate, assignment.Effect);
        Assert.Equal(MemoryRecordMutationProblem.LuaFailure, assignment.Problem);
        failingSetter.Execute("assert(assigned == 1)");
    }

    private sealed class MutationFixture : IDisposable
    {
        private readonly NativeLuaState _nativeState;
        private readonly HostScope _scope;

        public MutationFixture(bool parentSetterFails = false, bool destroyFails = false)
        {
            EngineTest.RequireNativeLua();
            _nativeState = new NativeLuaState();
            _scope = new HostScope(_nativeState);
            State = _scope.State;
            var listObject = FakeHost.CreateObject(State, "Probe");
            var childInitializer = parentSetterFails
                ? "o.props.ID = 1; o.setters.Parent = function(o, v) o.props.Parent = v; assigned = assigned + 1; error('set after side effect') end"
                : "o.props.ID = 1; o.setters.Parent = function(o, v) o.props.Parent = v; assigned = assigned + 1 end";
            var childObject = FakeHost.CreateObject(State, destroyFails ? "Stubborn" : "Probe", childInitializer);
            var parentObject = FakeHost.CreateObject(State, "Probe", "o.props.ID = 2");
            var grandparentObject = FakeHost.CreateObject(State, "Probe", "o.props.ID = 3");
            SetGlobal(State, "list"u8, new EngineAddressList(listObject));
            SetGlobal(State, "child"u8, new MemoryRecord(childObject));
            SetGlobal(State, "parent"u8, new MemoryRecord(parentObject));
            SetGlobal(State, "grandparent"u8, new MemoryRecord(grandparentObject));
            EngineTest.Run(State, """
                                  destroyed = 0
                                  assigned = 0
                                  function getAddressList() return list end
                                  list.getMemoryRecordByID = function(id)
                                    if id == 1 then return child end
                                    if id == 2 then return parent end
                                    if id == 3 then return grandparent end
                                    return nil
                                  end
                                  child.destroy = function() destroyed = destroyed + 1 end
                                  """u8);
        }

        public LuaState State { get; }

        public void Execute(string source)
        {
            EngineTest.Run(State, Encoding.UTF8.GetBytes(source));
        }

        public void Dispose()
        {
            _scope.Dispose();
            _nativeState.Dispose();
        }

        private static void SetGlobal<T>(LuaState state, ReadOnlySpan<byte> name, T value)
            where T : struct, ILuaMarshaller<T>
        {
            using LuaFrame frame = new(state);
            T.Push(state, value);
            var status = state.TrySetGlobal(name);
            if (!status.IsOk) Assert.Fail("Setting a test global failed: " + EngineTest.ErrorMessage(state, status));
        }
    }

    private static void AssertDeleteAddressListPreflight(string source, MemoryRecordMutationProblem expectedProblem)
    {
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, Encoding.UTF8.GetBytes(source));
        var top = L.Top;

        var outcome = AddressListMutations.Delete(new MemoryRecordId(1));

        Assert.Equal(MemoryRecordMutationEffect.NotAttempted, outcome.Effect);
        Assert.Equal(expectedProblem, outcome.Problem);
        Assert.Equal(top, L.Top);
    }
}
