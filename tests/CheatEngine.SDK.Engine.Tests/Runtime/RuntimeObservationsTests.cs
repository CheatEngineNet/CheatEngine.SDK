using System.Text;

using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Runtime;

/// <summary>
///     Native-Lua tests for the SDK-produced <see cref="RuntimeInfo" /> snapshot and for the read-only allowlist of every
///     runtime probe. The stand-ins return the spike C3 values; these are fixture contracts (C1/C2), not host
///     qualification.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class RuntimeObservationsTests
{
	[Fact]
	[Trait("Qualification", "Q32.a")]
	public void try_observe_runtime_info_with_the_spike_x64_facts_produces_a_complete_snapshot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 45052);

		ProcessOperationStatus status = RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info);

		Assert.True(status.IsSuccess);
		Assert.NotNull(info);
		Assert.Equal(CheatEngineVersion.Ce77010621, info.Version);
		Assert.Equal(CheatEngineArchitecture.X64, info.SystemArchitecture);
		Assert.Equal(CheatEngineArchitecture.X64, info.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, info.PointerSize);
		Assert.Equal(TargetAbi.Windows, info.TargetAbi);
		Assert.Equal(new CheatEngineHostObservation(CheatEngineVersion.Ce77010621, CheatEngineArchitecture.X64, true,
			CheatEngineOperatingSystem.Windows), info.Host);
		Assert.Equal(new TargetArchitectureObservation(new TargetProcessId(45052), TargetBackend.LocalProcess,
			PointerSize.Bit64, true, false, false, 0, 8), info.Target);

		RuntimeCapabilityId[] expected =
		[
			RuntimeCapabilityId.CheatEngineVersion, RuntimeCapabilityId.SystemArchitecture,
			RuntimeCapabilityId.CheatEngineBitness, RuntimeCapabilityId.OperatingSystem,
			RuntimeCapabilityId.CurrentProcess, RuntimeCapabilityId.TargetBackend,
			RuntimeCapabilityId.TargetArchitecture, RuntimeCapabilityId.TargetAndroid, RuntimeCapabilityId.TargetAbi,
			RuntimeCapabilityId.ConfiguredPointerSize
		];
		Assert.Equal(expected.Length, info.Capabilities.Count);
		foreach (RuntimeCapabilityId capability in expected)
		{
			Assert.True(info.Capabilities.TryGet(capability, out RuntimeCapabilityAvailability availability));
			Assert.Equal(RuntimeCapabilityAvailabilityState.Available, availability.State);
			Assert.Equal(CheatEngineVersion.Ce77010621, availability.Contract.MinimumCheatEngineVersion);
			Assert.Equal(RuntimeThreadRequirement.Unknown, availability.Contract.ThreadRequirement);
			Assert.Equal(RuntimeOwnership.None, availability.Contract.Ownership);
		}

		RuntimeCapabilityContract version = Contract(info, RuntimeCapabilityId.CheatEngineVersion);
		RuntimeCapabilityContract bitness = Contract(info, RuntimeCapabilityId.CheatEngineBitness);
		RuntimeCapabilityContract pointer = Contract(info, RuntimeCapabilityId.ConfiguredPointerSize);
		Assert.Equal(RuntimeReturnSemantics.OptionalValue, version.ReturnSemantics);
		Assert.Equal(RuntimeArchitectureScope.CheatEngine, bitness.ArchitectureScope);
		Assert.Equal(RuntimeArchitectureRequirement.X64, bitness.ArchitectureRequirement);
		Assert.Equal(RuntimeReturnSemantics.Value, bitness.ReturnSemantics);
		Assert.Equal(RuntimeArchitectureScope.Target, pointer.ArchitectureScope);
		Assert.Equal(RuntimeArchitectureRequirement.Unknown, pointer.ArchitectureRequirement);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
			info.Capabilities.GetState(RuntimeCapabilityId.ProcessSelection));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q31.a")]
	public void try_observe_runtime_info_with_a_configured_pointer_size_of_four_on_x64_reports_it_as_the_pointer_size_and_keeps_bitness()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 45052);
		EngineTest.Run(scope.State, "rt_pointer_size = 4"u8);

		ProcessOperationStatus status = RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info);

		Assert.True(status.IsSuccess);
		Assert.NotNull(info);
		Assert.Equal(PointerSize.Bit32, info.PointerSize);
		Assert.Equal(CheatEngineArchitecture.X64, info.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, info.Target?.Bitness);
		Assert.True(info.Target?.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void try_observe_runtime_info_without_a_target_keeps_host_facts_and_no_target_facts()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		// With no target CE 7.7 still reports x86 family, 64-bit and pointer size 8 (spike C3 D2): none may be read.
		FakeHost.InstallCe77X64TargetFacts(scope.State, 0);
		EngineTest.Run(scope.State, """
		                            function targetIs64Bit() error('no target fact without a target') end
		                            function getPointerSize() error('no target fact without a target') end
		                            function isConnectedToCEServer() error('no target fact without a target') end
		                            """u8);

		ProcessOperationStatus status = RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info);

		Assert.True(status.IsSuccess);
		Assert.NotNull(info);
		Assert.NotNull(info.Host);
		Assert.Null(info.Target);
		Assert.Equal(CheatEngineVersion.Ce77010621, info.Version);
		Assert.Equal(CheatEngineArchitecture.Unknown, info.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, info.PointerSize);
		Assert.Equal(TargetAbi.Unknown, info.TargetAbi);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			info.Capabilities.GetState(RuntimeCapabilityId.CurrentProcess));
		Assert.False(info.Capabilities.TryGet(RuntimeCapabilityId.TargetArchitecture, out _));
		Assert.False(info.Capabilities.TryGet(RuntimeCapabilityId.ConfiguredPointerSize, out _));
		Assert.False(info.Capabilities.TryGet(RuntimeCapabilityId.TargetBackend, out _));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.c")]
	public void try_observe_runtime_info_on_a_file_as_process_target_returns_no_snapshot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, "rt_process_id = 4294967295"u8);

		ProcessOperationStatus status = RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info);

		Assert.Equal(ProcessOperationStatusKind.FileAsProcessTarget, status.Kind);
		Assert.Null(info);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("function getPointerSize() rt_process_id = 77 return 8 end", ProcessOperationStatusKind.TargetChanged)]
	[InlineData("function getABI() error('fixture failure') end", ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData("function targetIsAndroid() return nil end", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("function getOperatingSystem() error('fixture failure') end",
		ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData("function cheatEngineIs64Bit() return nil end", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("rt_file_version_table.build = 1", ProcessOperationStatusKind.InvalidResult)]
	public void try_observe_runtime_info_keeps_target_change_lua_failure_and_malformed_results(string fixture,
		ProcessOperationStatusKind expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(fixture));

		ProcessOperationStatus status = RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info);

		Assert.Equal(expected, status.Kind);
		Assert.Null(info);
		if (expected == ProcessOperationStatusKind.ProtectedLuaFailure)
		{
			Assert.Equal(LuaStatus.RuntimeError, status.LuaStatus);
		}

		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void absent_globals_are_unavailable_and_unprobed_capabilities_are_absent_from_the_set()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		// Host: only getSystemArchitecture. Target: PID, the 64-bit flag and the x86 probe; no ARM, Android, ABI,
		// pointer-size or CEServer probe.
		EngineTest.Run(scope.State, """
		                            function getSystemArchitecture() return 1 end
		                            function getOpenedProcessID() return 4242 end
		                            function targetIs64Bit() return true end
		                            function targetIsX86() return true end
		                            """u8);

		ProcessOperationStatus status = RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info);

		Assert.True(status.IsSuccess);
		Assert.NotNull(info);
		RuntimeCapabilities capabilities = info.Capabilities;
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.CheatEngineVersion));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			capabilities.GetState(RuntimeCapabilityId.SystemArchitecture));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.CheatEngineBitness));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.OperatingSystem));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			capabilities.GetState(RuntimeCapabilityId.CurrentProcess));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.TargetBackend));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.TargetAndroid));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.TargetAbi));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			capabilities.GetState(RuntimeCapabilityId.ConfiguredPointerSize));
		Assert.False(capabilities.TryGet(RuntimeCapabilityId.ProcessSelection, out _));
		Assert.Equal(10, capabilities.Count);

		Assert.Null(info.Host?.FileVersion);
		Assert.Null(info.Host?.CheatEngineIs64Bit);
		Assert.Equal(default, info.Version);
		Assert.Equal(TargetBackend.Unknown, info.Target?.Backend);
		Assert.Null(info.Target?.IsArmFamily);
		Assert.Equal(CheatEngineArchitecture.Unknown, info.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, info.PointerSize);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("getOpenedProcessID = nil", "Process.Current")]
	[InlineData("targetIs64Bit = nil", "Runtime.TargetArchitecture")]
	public void an_absent_required_target_global_leaves_the_target_unobserved_and_its_capability_unavailable(
		string fixture, string capability)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(fixture));

		ProcessOperationStatus status = RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info);

		Assert.True(status.IsSuccess);
		Assert.NotNull(info);
		Assert.Null(info.Target);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable,
			info.Capabilities.GetState(new RuntimeCapabilityId(capability)));
		Assert.False(info.Capabilities.TryGet(RuntimeCapabilityId.ConfiguredPointerSize, out _));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q45")]
	public void runtime_probes_never_call_dbk_dbvm_open_process_or_setters()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallRecordingRuntimeGlobals(scope.State, 45052);

		Assert.True(RuntimeObservations.TryObserveRuntimeInfo(out RuntimeInfo? info).IsSuccess);
		Assert.True(RuntimeHostOperations.ObserveHost(out _).IsSuccess);
		Assert.True(RuntimeHostOperations.TryGetCheatEngineFileVersion(out _).IsSuccess);
		Assert.True(RuntimeHostOperations.TryGetSystemArchitecture(out _).IsSuccess);
		Assert.True(RuntimeHostOperations.TryIsCheatEngine64Bit(out _).IsSuccess);
		Assert.True(RuntimeHostOperations.TryGetOperatingSystem(out _).IsSuccess);
		Assert.True(RuntimeHostOperations.TryGetTargetAbi(out _).IsSuccess);
		Assert.True(RuntimeProcessOperations.ObserveTargetArchitecture(out _).IsSuccess);
		Assert.True(RuntimeProcessOperations.TryGetConfiguredPointerSize(out _, out _).IsSuccess);
		Assert.True(RuntimeProcessOperations.ObserveCurrent(out _).IsSuccess);
		Assert.Equal(InstructionOperationStatus.Success, InstructionProfiles.TryObserveCurrent(out _));

		IReadOnlyList<string> calls = FakeHost.RecordedRuntimeGlobals(scope.State);
		Assert.NotNull(info);
		Assert.NotEmpty(calls);
		Assert.All(calls, call => Assert.Contains(call, FakeHost.ReadOnlyRuntimeGlobals, StringComparer.Ordinal));
		Assert.DoesNotContain(calls, call => FakeHost.ForbiddenRuntimeGlobals.Contains(call, StringComparer.Ordinal));
		Assert.Empty(FakeHost.RecordedArityViolations(scope.State));
		Assert.Equal(FakeHost.ReadOnlyRuntimeGlobals.Order(StringComparer.Ordinal),
			calls.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void try_observe_runtime_info_on_a_detached_runtime_retains_the_lifecycle_admission_failure()
	{
		LuaRuntime.Detach();

		Assert.Throws<InvalidOperationException>(() => RuntimeObservations.TryObserveRuntimeInfo(out _));
		Assert.Throws<InvalidOperationException>(() => RuntimeHostOperations.ObserveHost(out _));
		Assert.Throws<InvalidOperationException>(() => RuntimeProcessOperations.ObserveTargetArchitecture(out _));
		Assert.Throws<InvalidOperationException>(() => RuntimeProcessOperations.TryGetConfiguredPointerSize(out _, out _));
	}

	private static RuntimeCapabilityContract Contract(RuntimeInfo info, RuntimeCapabilityId capability)
	{
		Assert.True(info.Capabilities.TryGet(capability, out RuntimeCapabilityAvailability availability));
		return availability.Contract;
	}
}
