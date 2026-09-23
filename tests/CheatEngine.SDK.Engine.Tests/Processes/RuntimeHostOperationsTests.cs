using System.Text;

using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Processes;

/// <summary>
///     Native-Lua boundary tests for the Cheat Engine host facts: bitness, operating system and the complete file
///     version. The stand-ins return the spike C3 values; they are fixture contracts, not host qualification.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class RuntimeHostOperationsTests
{
	[Fact]
	public void runtime_host_operations_decode_cheat_engine_bitness_operating_system_and_file_version()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);

		LuaOperationStatus bitnessStatus = RuntimeHostOperations.TryIsCheatEngine64Bit(out bool is64Bit);
		LuaOperationStatus systemStatus =
			RuntimeHostOperations.TryGetOperatingSystem(out CheatEngineOperatingSystem operatingSystem);
		LuaOperationStatus versionStatus =
			RuntimeHostOperations.TryGetCheatEngineFileVersion(out CheatEngineVersion version);

		Assert.True(bitnessStatus.IsSuccess);
		Assert.True(is64Bit);
		Assert.True(systemStatus.IsSuccess);
		Assert.Equal(CheatEngineOperatingSystem.Windows, operatingSystem);
		Assert.True(versionStatus.IsSuccess);
		Assert.Equal(CheatEngineVersion.Ce77010621, version);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("rt_file_version_table.build = 10620")]
	[InlineData("rt_file_version_table.major = 8")]
	[InlineData("rt_file_version_table.minor = nil")]
	[InlineData("rt_file_version_table.release = 0.0")]
	[InlineData("rt_file_version_table.build = '10621'")]
	[InlineData("rt_file_version = -1")]
	[InlineData("rt_file_version = 1970354901756285.0")]
	[InlineData("rt_file_version = '1970354901756285'")]
	[InlineData("rt_file_version_table = 'not a table'")]
	public void file_version_integer_and_table_must_agree(string change)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(change));

		LuaOperationStatus status = RuntimeHostOperations.TryGetCheatEngineFileVersion(out CheatEngineVersion version);

		Assert.Equal(LuaOperationStatusKind.InvalidResult, status.Kind);
		Assert.Equal(default, version);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void file_version_integer_without_a_table_is_accepted()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		EngineTest.Run(scope.State, "rt_file_version_table = nil"u8);

		LuaOperationStatus status = RuntimeHostOperations.TryGetCheatEngineFileVersion(out CheatEngineVersion version);

		Assert.True(status.IsSuccess);
		Assert.Equal(CheatEngineVersion.Ce77010621, version);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("function getCheatEngineFileVersion() end")]
	[InlineData("function getCheatEngineFileVersion() return nil end")]
	public void file_version_without_any_result_is_a_nil_result(string fixture)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(fixture));

		LuaOperationStatus status = RuntimeHostOperations.TryGetCheatEngineFileVersion(out CheatEngineVersion version);

		Assert.Equal(LuaOperationStatusKind.NilResult, status.Kind);
		Assert.Equal(default, version);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void file_version_and_other_host_globals_keep_absence_and_lua_failure_distinct()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);

		LuaOperationStatus absent = RuntimeHostOperations.TryGetCheatEngineFileVersion(out _);
		// Resolved globals are cached per state identity, so each stand-in reads a variable instead of being redefined.
		EngineTest.Run(scope.State, """
		                            bitness_result = nil
		                            function getCheatEngineFileVersion() error('fixture version failure') end
		                            function cheatEngineIs64Bit() return bitness_result end
		                            function getOperatingSystem() return 5 end
		                            """u8);
		LuaOperationStatus raising = RuntimeHostOperations.TryGetCheatEngineFileVersion(out _);
		LuaOperationStatus nilBitness = RuntimeHostOperations.TryIsCheatEngine64Bit(out bool is64Bit);
		LuaOperationStatus unknownSystem =
			RuntimeHostOperations.TryGetOperatingSystem(out CheatEngineOperatingSystem operatingSystem);
		EngineTest.Run(scope.State, "bitness_result = 'yes'"u8);
		LuaOperationStatus malformedBitness = RuntimeHostOperations.TryIsCheatEngine64Bit(out _);

		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, absent.Kind);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, raising.Kind);
		Assert.Equal(LuaStatus.RuntimeError, raising.LuaStatus);
		Assert.Equal(LuaOperationStatusKind.NilResult, nilBitness.Kind);
		Assert.False(is64Bit);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, unknownSystem.Kind);
		Assert.Equal(CheatEngineOperatingSystem.Unknown, operatingSystem);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, malformedBitness.Kind);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_host_reads_the_four_host_facts_without_inference()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);

		LuaOperationStatus status = RuntimeHostOperations.ObserveHost(out CheatEngineHostObservation host);

		Assert.True(status.IsSuccess);
		Assert.Equal(CheatEngineVersion.Ce77010621, host.FileVersion);
		Assert.Equal(CheatEngineArchitecture.X64, host.SystemArchitecture);
		Assert.True(host.CheatEngineIs64Bit);
		Assert.Equal(CheatEngineOperatingSystem.Windows, host.OperatingSystem);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_host_keeps_absent_globals_unknown_instead_of_inferring_them()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		// Only the system architecture is present: it must not fill the 64-bit flag.
		EngineTest.Run(scope.State, "function getSystemArchitecture() return 1 end"u8);

		LuaOperationStatus status = RuntimeHostOperations.ObserveHost(out CheatEngineHostObservation host);

		Assert.True(status.IsSuccess);
		Assert.Null(host.FileVersion);
		Assert.Equal(CheatEngineArchitecture.X64, host.SystemArchitecture);
		Assert.Null(host.CheatEngineIs64Bit);
		Assert.Equal(CheatEngineOperatingSystem.Unknown, host.OperatingSystem);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_host_with_an_unreadable_version_resource_keeps_the_other_facts()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		EngineTest.Run(scope.State, "function getCheatEngineFileVersion() end"u8);

		LuaOperationStatus status = RuntimeHostOperations.ObserveHost(out CheatEngineHostObservation host);

		Assert.True(status.IsSuccess);
		Assert.Null(host.FileVersion);
		Assert.True(host.CheatEngineIs64Bit);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("function getSystemArchitecture() error('fixture failure') end", LuaOperationStatusKind.LuaFailure)]
	[InlineData("function cheatEngineIs64Bit() error('fixture failure') end", LuaOperationStatusKind.LuaFailure)]
	[InlineData("function getOperatingSystem() error('fixture failure') end", LuaOperationStatusKind.LuaFailure)]
	[InlineData("function getCheatEngineFileVersion() error('fixture failure') end", LuaOperationStatusKind.LuaFailure)]
	[InlineData("function cheatEngineIs64Bit() return nil end", LuaOperationStatusKind.NilResult)]
	[InlineData("function getOperatingSystem() return nil end", LuaOperationStatusKind.NilResult)]
	[InlineData("function getSystemArchitecture() return 99 end", LuaOperationStatusKind.InvalidResult)]
	[InlineData("function getOperatingSystem() return 3 end", LuaOperationStatusKind.InvalidResult)]
	[InlineData("function cheatEngineIs64Bit() return 1 end", LuaOperationStatusKind.InvalidResult)]
	[InlineData("rt_file_version_table.major = 6", LuaOperationStatusKind.InvalidResult)]
	public void observe_host_keeps_raising_nil_and_malformed_results_distinct(string fixture,
		LuaOperationStatusKind expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(fixture));

		LuaOperationStatus status = RuntimeHostOperations.ObserveHost(out CheatEngineHostObservation host);

		Assert.Equal(expected, status.Kind);
		Assert.Equal(default, host);
		Assert.Equal(0, scope.State.Top);
	}
}
