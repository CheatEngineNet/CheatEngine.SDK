using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Tests.Scanning;

/// <summary>
///     CE 7.7-shaped Lua stand-ins for <c>createMemScan</c>, <c>createFoundList</c>, the MemScan members the SDK calls,
///     and the FoundList members it reads, over the normal fake-host userdata. Every call is appended to the Lua
///     <c>trace</c> table; tests steer the stand-ins through Lua globals, so one fixture covers every result shape.
/// </summary>
/// <remarks>
///     <para>Trace entries (in call order):</para>
///     <list type="bullet">
///         <item><c>factory.scan</c>, <c>factory.list</c>: the two CE factories.</item>
///         <item>
///             <c>scan.setOnlyOneResult:&lt;v&gt;</c>, <c>scan.first:&lt;argument count&gt;</c>,
///             <c>scan.next:&lt;argument count&gt;</c>, <c>scan.wait</c> (no argument) or <c>scan.wait:&lt;timeout&gt;</c>,
///             <c>scan.terminate:&lt;force&gt;</c>, <c>scan.new</c>, <c>scan.getOnlyResult</c>, <c>scan.ErrorString</c> (a
///             property read), <c>scan.set.&lt;property&gt;:&lt;v&gt;</c> (a write to <c>OnlyOneResult</c>,
///             <c>IsUnique</c>, <c>OnGuiUpdate</c>, <c>OnScanDone</c> or <c>OnScanStart</c>), <c>scan.destroy</c>.
///         </item>
///         <item>
///             <c>list.initialize</c>, <c>list.deinitialize</c>, <c>results.getCount</c>,
///             <c>results.getAddress:&lt;i&gt;</c>, <c>results.getValue:&lt;i&gt;</c>, <c>list.destroy</c>.
///         </item>
///     </list>
///     <para>Steering globals (all optional):</para>
///     <list type="bullet">
///         <item>
///             <c>scan_wait_mode</c> (default <c>'true'</c>) or a queue <c>scan_wait_modes</c>: <c>'true'</c>,
///             <c>'false'</c>, <c>'raise'</c>, <c>'nil'</c>, <c>'none'</c> (zero values), <c>'number'</c>,
///             <c>'string'</c>; <c>scan_wait_hook</c> runs inside the wait.
///         </item>
///         <item>
///             <c>scan_first_raises</c> with <c>scan_first_error_payload</c>, <c>scan_first_hook</c>,
///             <c>scan_next_raises</c> with <c>scan_next_error_payload</c>, <c>scan_set_only_one_raises</c>,
///             <c>scan_terminate_raises</c>, <c>scan_new_raises</c>, <c>scan_destroy_raises</c>.
///         </item>
///         <item>
///             <c>scan_error_mode</c> (<c>'string'</c> by default, <c>'nil'</c>, <c>'number'</c>, <c>'raise'</c>) and
///             <c>scan_error_string</c> (default empty) for the <c>ErrorString</c> property.
///         </item>
///         <item>
///             <c>only_result_mode</c> (<c>'none'</c> by default, <c>'nil'</c>, <c>'value'</c>, <c>'raise'</c>) and
///             <c>only_result_value</c> for <c>getOnlyResult</c>.
///         </item>
///         <item>
///             <c>found_addresses</c> (a Lua array of the values <c>getAddress</c> returns; a missing entry is
///             synthesized), <c>found_count</c> (overrides the count), <c>found_count_raises</c>,
///             <c>found_address_raises</c>, <c>list_initialize_raises</c>, <c>list_destroy_raises</c>.
///         </item>
///         <item>
///             <c>opened_process_id</c>: the selected target; <c>isConnectedToCEServer</c> returns
///             <see langword="false" />, as every fixture of a qualified local target does.
///         </item>
///     </list>
/// </remarks>
internal static class MemScanTestHost
{
	private const string ScannerInitializer = """
	                                          o.props.setOnlyOneResult = function(...)
	                                            set_only_one_argument_count = select('#', ...)
	                                            table.insert(trace, 'scan.setOnlyOneResult:' .. tostring((...)))
	                                            if scan_set_only_one_raises then error('setOnlyOneResult rejected') end
	                                          end
	                                          o.props.firstScan = function(...)
	                                            first_scan_args = table.pack(...)
	                                            first_scan_start_type = math.type(first_scan_args[6])
	                                            first_scan_stop_type = math.type(first_scan_args[7])
	                                            table.insert(trace, 'scan.first:' .. first_scan_args.n)
	                                            if scan_first_hook then scan_first_hook() end
	                                            if scan_first_raises then error(scan_first_error_payload) end
	                                          end
	                                          o.props.nextScan = function(...)
	                                            next_scan_args = table.pack(...)
	                                            table.insert(trace, 'scan.next:' .. next_scan_args.n)
	                                            if scan_next_raises then error(scan_next_error_payload) end
	                                          end
	                                          o.props.waitTillDone = function(...)
	                                            local n = select('#', ...)
	                                            wait_calls = (wait_calls or 0) + 1
	                                            wait_argument_count = n
	                                            wait_argument = ...
	                                            wait_argument_type = math.type((...))
	                                            if n == 0 then table.insert(trace, 'scan.wait')
	                                            else table.insert(trace, 'scan.wait:' .. tostring((...))) end
	                                            if scan_wait_hook then scan_wait_hook() end
	                                            local mode = scan_wait_mode or 'true'
	                                            if scan_wait_modes and #scan_wait_modes > 0 then mode = table.remove(scan_wait_modes, 1) end
	                                            if mode == 'true' then return true end
	                                            if mode == 'false' then return false end
	                                            if mode == 'raise' then error('waitTillDone rejected') end
	                                            if mode == 'nil' then return nil end
	                                            if mode == 'none' then return end
	                                            if mode == 'number' then return 1 end
	                                            if mode == 'string' then return 'true' end
	                                            error('unknown wait mode ' .. tostring(mode))
	                                          end
	                                          o.props.terminateScan = function(...)
	                                            terminate_argument_count = select('#', ...)
	                                            terminate_argument = ...
	                                            table.insert(trace, 'scan.terminate:' .. tostring((...)))
	                                            if scan_terminate_raises then error('terminateScan rejected') end
	                                          end
	                                          o.props.newScan = function()
	                                            table.insert(trace, 'scan.new')
	                                            if scan_new_raises then error('newScan rejected') end
	                                          end
	                                          o.props.getOnlyResult = function(...)
	                                            only_result_argument_count = select('#', ...)
	                                            table.insert(trace, 'scan.getOnlyResult')
	                                            local mode = only_result_mode or 'none'
	                                            if mode == 'none' then return end
	                                            if mode == 'nil' then return nil end
	                                            if mode == 'raise' then error('getOnlyResult rejected') end
	                                            return only_result_value
	                                          end
	                                          o.getters.ErrorString = function(o)
	                                            table.insert(trace, 'scan.ErrorString')
	                                            local mode = scan_error_mode or 'string'
	                                            if mode == 'raise' then error('ErrorString unreadable') end
	                                            if mode == 'nil' then return nil end
	                                            if mode == 'number' then return 42 end
	                                            return scan_error_string or ''
	                                          end
	                                          for _, name in ipairs({ 'OnlyOneResult', 'IsUnique', 'OnGuiUpdate', 'OnScanDone', 'OnScanStart' }) do
	                                            o.setters[name] = function(o, value)
	                                              table.insert(trace, 'scan.set.' .. name .. ':' .. tostring(value))
	                                              o.props[name] = value
	                                            end
	                                          end
	                                          o.getters.destroy = function(o)
	                                            return function()
	                                              o.destroyed = true
	                                              table.insert(trace, 'scan.destroy')
	                                              if scan_destroy_raises then error('scan destroy rejected') end
	                                            end
	                                          end
	                                          """;

	private const string FoundListInitializer = """
	                                            o.props.initialize = function()
	                                              table.insert(trace, 'list.initialize')
	                                              if list_initialize_raises then error('initialize rejected') end
	                                            end
	                                            o.props.deinitialize = function() table.insert(trace, 'list.deinitialize') end
	                                            o.props.getCount = function()
	                                              table.insert(trace, 'results.getCount')
	                                              if found_count_raises then error('getCount rejected') end
	                                              if found_count ~= nil then return found_count end
	                                              return #found_addresses
	                                            end
	                                            o.getters.Count = function(o)
	                                              if found_count ~= nil then return found_count end
	                                              return #found_addresses
	                                            end
	                                            o.props.getAddress = function(index)
	                                              table.insert(trace, 'results.getAddress:' .. index)
	                                              if found_address_raises then error('getAddress rejected') end
	                                              local value = found_addresses[index + 1]
	                                              if value == nil then return string.format('%X', 0x10000 + index) end
	                                              return value
	                                            end
	                                            o.props.getValue = function(index)
	                                              table.insert(trace, 'results.getValue:' .. index)
	                                              return '100'
	                                            end
	                                            o.getters.destroy = function(o)
	                                              return function()
	                                                o.destroyed = true
	                                                table.insert(trace, 'list.destroy')
	                                                if list_destroy_raises then error('list destroy rejected') end
	                                              end
	                                            end
	                                            """;

	private static ReadOnlySpan<byte> Factories => """
	                                               function createMemScan(...)
	                                                 create_mem_scan_argument_count = select('#', ...)
	                                                 table.insert(trace, 'factory.scan')
	                                                 return factory_scan
	                                               end
	                                               function createFoundList(...)
	                                                 create_found_list_argument_count = select('#', ...)
	                                                 table.insert(trace, 'factory.list')
	                                                 return factory_found_list
	                                               end
	                                               function getOpenedProcessID() return opened_process_id end
	                                               function isConnectedToCEServer() return false end
	                                               """u8;

	/// <summary>
	///     Creates the scanner and found-list stand-ins, installs the two factories and selects the current process as
	///     the qualified local target.
	/// </summary>
	public static HostObjects Install(LuaState state)
	{
		EngineTest.Run(state, "trace = {}; found_addresses = {}"u8);
		Run(state, "opened_process_id = " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
		CEObject scanner = FakeHost.CreateObject(state, "Object", ScannerInitializer);
		CEObject foundList = FakeHost.CreateObject(state, "Object", FoundListInitializer);
		SetGlobalObject(state, "factory_scan"u8, scanner);
		SetGlobalObject(state, "factory_found_list"u8, foundList);
		EngineTest.Run(state, Factories);
		return new HostObjects(scanner, foundList);
	}

	/// <summary>
	///     Installs the stand-ins, creates a session through the production factory and clears the trace, so a test
	///     traces only its own session calls.
	/// </summary>
	public static MemoryScanSession CreateSession(LuaState state)
	{
		_ = Install(state);
		MemoryScanCreationOutcome outcome = MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? session);
		Assert.Equal(MemoryScanCreationStatus.Success, outcome.Status);
		ClearTrace(state);
		return Assert.IsType<MemoryScanSession>(session);
	}

	/// <summary>Runs a Lua chunk given as a managed string (UTF-8 encoded), failing the test on error.</summary>
	public static void Run(LuaState state, string source)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes(source));
	}

	/// <summary>The comma-joined trace.</summary>
	public static string ReadTrace(LuaState state)
	{
		using LuaFrame frame = new(state);
		EngineTest.Run(state, "return table.concat(trace, ',')"u8, 1);
		return EngineTest.ReadString(state, -1);
	}

	/// <summary>Empties the trace.</summary>
	public static void ClearTrace(LuaState state)
	{
		EngineTest.Run(state, "trace = {}"u8);
	}

	/// <summary>Evaluates a Lua expression that must produce <see langword="true" />.</summary>
	public static void AssertLua(LuaState state, string condition)
	{
		using LuaFrame frame = new(state);
		EngineTest.Run(state, Encoding.UTF8.GetBytes("return " + condition), 1);
		Assert.True(state.ToBoolean(-1), "Lua condition failed: " + condition);
	}

	/// <summary>Evaluates a Lua expression that must produce an integer.</summary>
	public static long ReadInteger(LuaState state, string expression)
	{
		using LuaFrame frame = new(state);
		EngineTest.Run(state, Encoding.UTF8.GetBytes("return " + expression), 1);
		return EngineTest.ReadInteger(state, -1);
	}

	private static void SetGlobalObject(LuaState state, ReadOnlySpan<byte> name, CEObject value)
	{
		using LuaFrame frame = new(state);
		value.Push(state);
		Assert.True(state.TrySetGlobal(name).IsOk);
	}

	/// <summary>The two host objects the factories return.</summary>
	[StructLayout(LayoutKind.Auto)]
	internal readonly record struct HostObjects(CEObject Scanner, CEObject FoundList);
}
