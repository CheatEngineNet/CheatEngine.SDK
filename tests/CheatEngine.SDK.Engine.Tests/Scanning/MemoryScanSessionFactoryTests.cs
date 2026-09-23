using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning;

/// <summary>Fixture contracts for the production-owned MemScan/FoundList factory and its rollback behavior.</summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryScanSessionFactoryTests
{
	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreateWithOutcome_when_both_factories_return_host_objects_transfers_ownership_to_the_session()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		CEObject scanner = CreateScanner(L);
		CEObject foundList = CreateFoundList(L);
		InstallFactories(L, scanner, foundList);

		MemoryScanCreationOutcome outcome = MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? created);

		Assert.Equal(MemoryScanCreationStatus.Success, outcome.Status);
		Assert.Equal(TargetSelectionObservationStatus.CurrentTargetQualified, outcome.TargetObservation.Status);
		Assert.Equal(Environment.ProcessId, outcome.TargetObservation.SelectedProcessId);
		Assert.True(outcome.TargetObservation.Incarnation.HasValue);
		Assert.Equal(Environment.ProcessId, outcome.TargetObservation.Incarnation.Value.ProcessId);
		MemoryScanSession session = Assert.IsType<MemoryScanSession>(created);
		Assert.Equal(scanner, session.Scanner.Handle);
		Assert.Equal(0, L.Top);

		session.Dispose();

		Assert.True(FakeHost.IsDestroyed(L, foundList));
		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,factory.list,list.destroy,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreate_when_the_child_factory_is_unavailable_rolls_back_the_created_parent()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		CEObject scanner = CreateScanner(L);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		EngineTest.Run(L,
			"trace = {}; function createMemScan() table.insert(trace, 'factory.scan'); return factory_scan end"u8);

		Assert.Equal(MemoryScanCreationStatus.GlobalUnavailable,
			MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created));
		Assert.Null(created);

		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreate_when_the_child_factory_raises_rolls_back_the_created_parent_and_restores_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		CEObject scanner = CreateScanner(L);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		EngineTest.Run(L, """
		                  trace = {}
		                  function createMemScan()
		                    table.insert(trace, 'factory.scan')
		                    return factory_scan
		                  end
		                  function createFoundList(scan)
		                    table.insert(trace, 'factory.list')
		                    error('found-list creation failed')
		                  end
		                  """u8);

		Assert.Equal(MemoryScanCreationStatus.LuaFailure,
			MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created));
		Assert.Null(created);

		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,factory.list,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreate_when_the_child_factory_returns_a_nonobject_rolls_back_the_created_parent()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		CEObject scanner = CreateScanner(L);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		EngineTest.Run(L, """
		                  trace = {}
		                  function createMemScan()
		                    table.insert(trace, 'factory.scan')
		                    return factory_scan
		                  end
		                  function createFoundList(scan)
		                    table.insert(trace, 'factory.list')
		                    return 42
		                  end
		                  """u8);

		Assert.Equal(MemoryScanCreationStatus.InvalidFoundListResult,
			MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created));
		Assert.Null(created);
		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,factory.list,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreate_when_the_child_factory_aliases_the_parent_rolls_back_without_creating_a_second_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		CEObject scanner = CreateScanner(L);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		EngineTest.Run(L, """
		                  trace = {}
		                  function createMemScan()
		                    table.insert(trace, 'factory.scan')
		                    return factory_scan
		                  end
		                  function createFoundList(scan)
		                    table.insert(trace, 'factory.list')
		                    return factory_scan
		                  end
		                  """u8);

		Assert.Equal(MemoryScanCreationStatus.AliasedFoundList,
			MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created));
		Assert.Null(created);

		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,factory.list,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreate_when_internal_adoption_fails_rolls_back_the_child_before_the_parent()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject scanner = CreateScanner(L);
		CEObject foundList = CreateFoundList(L);
		InstallFactories(L, scanner, foundList);

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
			MemoryScanSessions.TryCreateCore(
				out _, static (_, _) => throw new InvalidOperationException("injected adoption failure")));

		Assert.Equal("injected adoption failure", failure.Message);
		Assert.True(FakeHost.IsDestroyed(L, foundList));
		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,factory.list,list.destroy,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreate_when_the_parent_factory_returns_a_nonobject_does_not_publish_a_session()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		EngineTest.Run(L, "function createMemScan() return 42 end"u8);

		Assert.Equal(MemoryScanCreationStatus.InvalidScannerResult,
			MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created));
		Assert.Null(created);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreateDetailed_when_a_factory_returns_nil_keeps_absence_distinct_from_a_Lua_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		InstallCurrentTarget(L);
		EngineTest.Run(L, "function createMemScan() return nil end"u8);

		MemoryScanCreationStatus status = MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created);

		Assert.Equal(MemoryScanCreationStatus.NoScannerResult, status);
		Assert.Null(created);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreateDetailed_when_the_child_factory_returns_nil_reports_absence_and_releases_the_parent()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject scanner = CreateScanner(L);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		EngineTest.Run(L, Encoding.UTF8.GetBytes($$"""
		                                           trace = {}
		                                           function createMemScan()
		                                             table.insert(trace, 'factory.scan')
		                                             return factory_scan
		                                           end
		                                           function createFoundList(scan)
		                                             table.insert(trace, 'factory.list')
		                                             return nil
		                                           end
		                                           function getOpenedProcessID()
		                                             return {{Environment.ProcessId}}
		                                           end
		                                           function isConnectedToCEServer() return false end
		                                           """));

		MemoryScanCreationStatus status = MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created);

		Assert.Equal(MemoryScanCreationStatus.NoFoundListResult, status);
		Assert.Null(created);
		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,factory.list,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreateDetailed_when_rollback_destroy_is_not_confirmed_reports_that_fact_without_retrying()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject scanner = FakeHost.CreateObject(L, "Object", """
		                                                      o.getters.destroy = function(o)
		                                                        return function()
		                                                          table.insert(trace, 'scan.destroy')
		                                                          error('fixture destroy failure')
		                                                        end
		                                                      end
		                                                      """);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		EngineTest.Run(L, Encoding.UTF8.GetBytes($$"""
		                                           trace = {}
		                                           function createMemScan()
		                                             table.insert(trace, 'factory.scan')
		                                             return factory_scan
		                                           end
		                                           function createFoundList(scan)
		                                             table.insert(trace, 'factory.list')
		                                             return nil
		                                           end
		                                           function getOpenedProcessID()
		                                             return {{Environment.ProcessId}}
		                                           end
		                                           function isConnectedToCEServer() return false end
		                                           """));

		MemoryScanCreationStatus status = MemoryScanSessions.TryCreateDetailed(out MemoryScanSession? created);

		Assert.Equal(MemoryScanCreationStatus.RollbackUnconfirmed, status);
		Assert.Null(created);
		Assert.Equal("factory.scan,factory.list,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void TryCreateWithOutcome_refuses_an_unqualified_target_before_either_factory_acquires_an_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject scanner = CreateScanner(L);
		CEObject foundList = CreateFoundList(L);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		SetGlobalObject(L, "factory_found_list"u8, foundList);
		EngineTest.Run(L, """
		                  trace = {}
		                  function getOpenedProcessID() return 0 end
		                  function createMemScan() table.insert(trace, 'factory.scan'); return factory_scan end
		                  function createFoundList(scan) table.insert(trace, 'factory.list'); return factory_found_list end
		                  """u8);

		MemoryScanCreationOutcome outcome = MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? created);

		Assert.Equal(MemoryScanCreationStatus.TargetIdentityUnavailable, outcome.Status);
		Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, outcome.TargetObservation.Status);
		Assert.Null(created);
		Assert.False(FakeHost.IsDestroyed(L, scanner));
		Assert.False(FakeHost.IsDestroyed(L, foundList));
		Assert.Equal(string.Empty, ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void
		TryCreate_when_adoption_fails_and_the_child_rollback_raises_still_destroys_the_parent_once_and_rethrows_the_adoption_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject scanner = CreateScanner(L);
		CEObject foundList = FakeHost.CreateObject(L, "Object", """
		                                                        o.getters.destroy = function(o)
		                                                          return function()
		                                                            table.insert(trace, 'list.destroy')
		                                                            error('fixture child destroy failure')
		                                                          end
		                                                        end
		                                                        """);
		InstallFactories(L, scanner, foundList);
		InvalidOperationException injected = new("injected adoption failure");

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
			MemoryScanSessions.TryCreateCore(out _, (_, _) => throw injected));

		Assert.Same(injected, failure);
		Assert.True(FakeHost.IsDestroyed(L, scanner));
		Assert.Equal("factory.scan,factory.list,list.destroy,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void
		TryCreate_when_the_parent_rollback_raises_after_a_child_failure_reports_rollback_unconfirmed_without_retrying()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject scanner = FakeHost.CreateObject(L, "Object", """
		                                                      o.getters.destroy = function(o)
		                                                        return function()
		                                                          table.insert(trace, 'scan.destroy')
		                                                          error('fixture parent destroy failure')
		                                                        end
		                                                      end
		                                                      """);
		SetGlobalObject(L, "factory_scan"u8, scanner);
		EngineTest.Run(L, Encoding.UTF8.GetBytes($$"""
		                                           trace = {}
		                                           function createMemScan()
		                                             table.insert(trace, 'factory.scan')
		                                             return factory_scan
		                                           end
		                                           function createFoundList(scan)
		                                             table.insert(trace, 'factory.list')
		                                             error('found-list creation failed')
		                                           end
		                                           function getOpenedProcessID()
		                                             return {{Environment.ProcessId}}
		                                           end
		                                           function isConnectedToCEServer() return false end
		                                           """));

		MemoryScanCreationOutcome outcome = MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? created);

		Assert.Equal(MemoryScanCreationStatus.RollbackUnconfirmed, outcome.Status);
		Assert.Null(created);
		Assert.Equal("factory.scan,factory.list,scan.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	private static CEObject CreateScanner(LuaState state)
	{
		return FakeHost.CreateObject(state, "Object", """
		                                              o.getters.destroy = function(o)
		                                                return function()
		                                                  o.destroyed = true
		                                                  table.insert(trace, 'scan.destroy')
		                                                end
		                                              end
		                                              """);
	}

	private static CEObject CreateFoundList(LuaState state)
	{
		return FakeHost.CreateObject(state, "Object", """
		                                              o.getters.destroy = function(o)
		                                                return function()
		                                                  o.destroyed = true
		                                                  table.insert(trace, 'list.destroy')
		                                                end
		                                              end
		                                              """);
	}

	private static void InstallFactories(LuaState state, CEObject scanner, CEObject foundList)
	{
		SetGlobalObject(state, "factory_scan"u8, scanner);
		SetGlobalObject(state, "factory_found_list"u8, foundList);
		EngineTest.Run(state, Encoding.UTF8.GetBytes($$"""
		                                               trace = {}
		                                               function createMemScan()
		                                                 table.insert(trace, 'factory.scan')
		                                                 return factory_scan
		                                               end
		                                               function createFoundList(scan)
		                                                 table.insert(trace, 'factory.list')
		                                                 return factory_found_list
		                                               end
		                                               function getOpenedProcessID()
		                                                 return {{Environment.ProcessId}}
		                                               end
		                                               function isConnectedToCEServer() return false end
		                                               """));
	}

	private static void InstallCurrentTarget(LuaState state)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes("function getOpenedProcessID() return " +
													 Environment.ProcessId.ToString(CultureInfo.InvariantCulture) +
													 " end"));
		EngineTest.Run(state, FakeHost.LocalTargetBackendChunk);
	}

	private static void SetGlobalObject(LuaState state, ReadOnlySpan<byte> name, CEObject value)
	{
		using LuaFrame frame = new(state);
		CEObject.Push(state, value);
		Assert.True(state.TrySetGlobal(name).IsOk);
	}

	private static string ReadTrace(LuaState state)
	{
		using LuaFrame frame = new(state);
		EngineTest.Run(state, "return table.concat(trace, ',')"u8, 1);
		return EngineTest.ReadString(state, -1);
	}
}
