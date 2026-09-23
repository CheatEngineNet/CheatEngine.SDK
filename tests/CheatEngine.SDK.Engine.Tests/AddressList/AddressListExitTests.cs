using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tables;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Tests.AddressList;

/// <summary>
///     The exit battery of audit chapter 14 for address-list records: identifiers after a reload, index bounds, symbolic
///     addresses, destroyed parents, double deletes, mutations during a table load, and the runtime identity.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AddressListExitTests
{
	[Fact]
	[Trait("Qualification", "Q34")]
	public void Record_id_is_not_found_after_a_table_reload()
	{
		using AddressListTestHost host = new();
		CEObject before = host.AddRecord(1);
		MemoryRecord borrowed = new(before);

		// A reload replaces every record: the old identifier is gone.
		host.Execute("records = {}");
		MemoryRecordMutationOutcome missing = AddressListMutations.Delete(new MemoryRecordId(1));
		MemoryRecordActivationOutcome notActivated = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordMutationProblem.RecordNotFound, missing.Problem);
		Assert.Equal(MemoryRecordMutationProblem.RecordNotFound, notActivated.Problem);

		// The reloaded table reuses the identifier for a new record: a command resolves the new record, never the
		// old borrowed handle.
		CEObject after = host.AddRecord(1);
		MemoryRecordMutationOutcome deleted = AddressListMutations.Delete(new MemoryRecordId(1));

		Assert.True(deleted.IsCompleted);
		host.Execute("assert(delete_calls == 1)");
		FakeHost.RunOnObject(host.State, after, "assert(o.gone == true)");
		FakeHost.RunOnObject(host.State, before, "assert(o.gone == nil)");
		Assert.NotEqual(new MemoryRecord(after), borrowed);
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void Child_index_out_of_bounds_returns_false_without_a_lua_error()
	{
		using AddressListTestHost host = new();
		CEObject child = host.AddRecord(2);
		CEObject parent = host.AddRecord(1);
		FakeHost.SetGlobalObject(host.State, "only_child", child);
		FakeHost.RunOnObject(host.State, parent, "o.items[1] = only_child");
		MemoryRecord record = new(parent);

		Assert.True(record.TryGetChild(0, out MemoryRecord first));
		Assert.Equal(new MemoryRecord(child), first);
		Assert.False(record.TryGetChild(1, out MemoryRecord missing));
		Assert.True(missing.IsNull);
		Assert.False(record.TryGetChild(int.MaxValue, out _));
		Assert.Throws<ArgumentOutOfRangeException>(() => record.TryGetChild(-1, out _));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void Symbolic_address_expression_stays_distinct_from_the_resolved_current_address()
	{
		using AddressListTestHost host = new();
		CEObject handle = host.AddRecord(1, extra: """
		                                           o.props.Address = "game.exe+10"
		                                           o.getters.getCurrentAddress = function() return function() return 0x140000010 end end
		                                           """);
		MemoryRecord record = new(handle);

		Assert.True(record.TryGetAddressExpression(out string? expression));
		Assert.True(record.TryGetCurrentAddress(out Address current));

		Assert.Equal("game.exe+10", expression);
		Assert.Equal(new Address(0x140000010), current);
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void Set_parent_to_a_deleted_parent_reports_parent_not_found()
	{
		using AddressListTestHost host = new();
		CEObject child = host.AddRecord(1);
		host.AddRecord(2);

		Assert.True(AddressListMutations.Delete(new MemoryRecordId(2)).IsCompleted);
		MemoryRecordMutationOutcome outcome =
			AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(2));

		Assert.Equal(MemoryRecordMutationEffect.NotAttempted, outcome.Effect);
		Assert.Equal(MemoryRecordMutationProblem.ParentNotFound, outcome.Problem);
		FakeHost.RunOnObject(host.State, child, "assert(o.props.Parent == nil)");
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void Double_delete_reports_not_found_and_destroys_once()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1);

		MemoryRecordMutationOutcome first = AddressListMutations.Delete(new MemoryRecordId(1));
		MemoryRecordMutationOutcome second = AddressListMutations.Delete(new MemoryRecordId(1));

		Assert.True(first.IsCompleted);
		Assert.Equal(MemoryRecordMutationEffect.NotAttempted, second.Effect);
		Assert.Equal(MemoryRecordMutationProblem.RecordNotFound, second.Problem);
		Assert.Equal(1, host.ReadInteger("delete_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void Mutation_during_a_table_load_is_refused()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1);
		host.AddRecord(2);
		List<MemoryRecordMutationOutcome> mutations = [];
		List<MemoryRecordActivationOutcome> activations = [];
		using (FakeHost.InstallReentrantHook(host.State, "table_script", () =>
			   {
				   mutations.Add(AddressListMutations.Delete(new MemoryRecordId(1)));
				   mutations.Add(AddressListMutations.SetParent(new MemoryRecordId(1), new MemoryRecordId(2)));
				   activations.Add(AddressListMutations.SetActive(new MemoryRecordId(1), true));
			   }))
		{
			host.Execute("loadTable = function(path, merge) table_script() end");

			LuaOperationStatus load = CheatTableFiles.TryLoad("scripted.ct", false);

			Assert.True(load.IsSuccess);
		}

		Assert.Null(FakeHost.TakeReentrantHookFailure());
		Assert.Equal(2, mutations.Count);
		Assert.All(mutations, static outcome =>
		{
			Assert.Equal(MemoryRecordMutationEffect.NotAttempted, outcome.Effect);
			Assert.Equal(MemoryRecordMutationProblem.TableLoadInProgress, outcome.Problem);
		});
		MemoryRecordActivationOutcome activation = Assert.Single(activations);
		Assert.Equal(MemoryRecordActivationOutcomeKind.NotAttempted, activation.Kind);
		Assert.Equal(MemoryRecordMutationProblem.TableLoadInProgress, activation.Problem);
		Assert.Equal(0, host.ReadInteger("delete_calls"));
		Assert.Equal(0, host.ReadInteger("active_set_calls"));

		// The load scope ended with the load: the same commands now run.
		Assert.True(AddressListMutations.Delete(new MemoryRecordId(1)).IsCompleted);
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void A_state_replacement_cannot_interleave_with_an_admitted_mutation()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1);
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;
		using (FakeHost.InstallReentrantHook(host.State, "replace_state", FakeHost.ReplaceStateGeneration))
		{
			// The record lookup runs Lua inside the command's admitted operation and tries to replace the state there.
			host.Execute("""
			             local lookup = list.getMemoryRecordByID
			             list.getMemoryRecordByID = function(id) replace_state() return lookup(id) end
			             """);

			MemoryRecordMutationOutcome outcome = AddressListMutations.Delete(new MemoryRecordId(1));

			Assert.True(outcome.IsCompleted);
		}

		Assert.IsType<InvalidOperationException>(FakeHost.TakeReentrantHookFailure());
		Assert.Equal(before, LuaRuntime.CurrentStateIdentity);
		Assert.Equal(1, host.ReadInteger("delete_calls"));
		Assert.Equal(0, host.State.Top);
	}
}
