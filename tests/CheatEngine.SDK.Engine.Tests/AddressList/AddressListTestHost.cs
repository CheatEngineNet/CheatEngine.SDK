using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.AddressList;

/// <summary>
///     A Cheat Engine address list double whose records are found by identifier through a Lua <c>records</c> table, as
///     <c>getMemoryRecordByID</c> does. A record's <c>destroy</c> removes it from the list and counts in
///     <c>delete_calls</c>; its <c>Active</c> setter counts in <c>active_set_calls</c> and follows the record's
///     <c>mode</c>: <c>apply</c>, <c>refuse</c>, <c>raise</c>, <c>async</c>, <c>retry-request</c> or
///     <c>post-read-fails</c>.
/// </summary>
internal sealed class AddressListTestHost : IDisposable
{
	private readonly NativeLuaState _nativeState;

	public AddressListTestHost()
	{
		EngineTest.RequireNativeLua();
		_nativeState = new NativeLuaState();
		Scope = new HostScope(_nativeState);
		State = Scope.State;
		CEObject list = FakeHost.CreateObject(State, "Probe");
		FakeHost.SetGlobalObject(State, "list", list);
		EngineTest.Run(State, """
		                      records = {}
		                      delete_calls = 0
		                      active_set_calls = 0
		                      retry_requested = 0
		                      function getAddressList() return list end
		                      list.getMemoryRecordByID = function(id) return records[id] end
		                      """u8);
	}

	public LuaState State
	{
		get;
	}

	public HostScope Scope
	{
		get;
	}

	public void Dispose()
	{
		Scope.Dispose();
		_nativeState.Dispose();
	}

	/// <summary>Creates a record with <paramref name="id" />, adds it to <c>records</c> and returns its handle.</summary>
	public CEObject AddRecord(int id, string mode = "apply", bool active = false, string extra = "")
	{
		string initializer = string.Create(CultureInfo.InvariantCulture, $$"""
		                                                                   o.props.ID = {{id}}
		                                                                   o.props.Active = {{(active ? "true" : "false")}}
		                                                                   o.props.AsyncProcessing = false
		                                                                   o.props.Async = false
		                                                                   o.mode = "{{mode}}"
		                                                                   o.getters.destroy = function(o)
		                                                                     return function()
		                                                                       delete_calls = delete_calls + 1
		                                                                       if o.gone then error("object already destroyed") end
		                                                                       o.gone = true
		                                                                       records[o.props.ID] = nil
		                                                                     end
		                                                                   end
		                                                                   o.setters.Active = function(o, value)
		                                                                     active_set_calls = active_set_calls + 1
		                                                                     if o.mode == "refuse" then return end
		                                                                     if o.mode == "retry-request" then retry_requested = retry_requested + 1 return end
		                                                                     o.props.Active = value
		                                                                     if o.mode == "raise" then error("activation raised after it started") end
		                                                                     if o.mode == "async" then o.props.AsyncProcessing = true end
		                                                                     if o.mode == "post-read-fails" then o.getters.Active = function() error("record vanished") end end
		                                                                   end
		                                                                   {{extra}}
		                                                                   """);
		CEObject record = FakeHost.CreateObject(State, "Probe", initializer);
		FakeHost.SetGlobalObject(State, "new_record", record);
		EngineTest.Run(State, Encoding.UTF8.GetBytes(
			"records[" + id.ToString(CultureInfo.InvariantCulture) + "] = new_record; new_record = nil"));
		return record;
	}

	/// <summary>Runs a Lua chunk against the fixture state.</summary>
	public void Execute(string source)
	{
		EngineTest.Run(State, Encoding.UTF8.GetBytes(source));
	}

	/// <summary>Reads an integer global.</summary>
	public long ReadInteger(string name)
	{
		using LuaFrame frame = new(State);
		Assert.True(State.TryGetGlobal(Encoding.UTF8.GetBytes(name)).IsOk);
		return EngineTest.ReadInteger(State, -1);
	}
}
