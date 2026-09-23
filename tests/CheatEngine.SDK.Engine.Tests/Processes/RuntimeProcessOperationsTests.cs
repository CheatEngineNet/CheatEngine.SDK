using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Processes;

/// <summary>Native-Lua boundary tests for the CE runtime and current-process semantic operations.</summary>
[Trait("Category", "NativeLua")]
public sealed class RuntimeProcessOperationsTests
{
	[Fact]
	public void RuntimeHostOperations_known_globals_decode_copied_runtime_facts()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		EngineTest.Run(lua, """
		                    function getCEVersion() return 7.7 end
		                    function getSystemArchitecture() return 1 end
		                    function getABI() return 0 end
		                    """u8);

		LuaOperationStatus versionStatus = RuntimeHostOperations.TryGetCheatEngineVersion(out double version);
		LuaOperationStatus architectureStatus =
			RuntimeHostOperations.TryGetSystemArchitecture(out CheatEngineArchitecture architecture);
		LuaOperationStatus abiStatus = RuntimeHostOperations.TryGetTargetAbi(out TargetAbi abi);

		Assert.True(versionStatus.IsSuccess);
		Assert.Equal(7.7d, version);
		Assert.True(architectureStatus.IsSuccess);
		Assert.Equal(CheatEngineArchitecture.X64, architecture);
		Assert.True(abiStatus.IsSuccess);
		Assert.Equal(TargetAbi.Windows, abi);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void RuntimeHostOperations_unknown_discriminants_are_invalid_and_leave_decoded_values_unknown()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		EngineTest.Run(lua, """
		                    function getSystemArchitecture() return 99 end
		                    function getABI() return 99 end
		                    """u8);

		LuaOperationStatus architectureStatus =
			RuntimeHostOperations.TryGetSystemArchitecture(out CheatEngineArchitecture architecture);
		LuaOperationStatus abiStatus = RuntimeHostOperations.TryGetTargetAbi(out TargetAbi abi);

		Assert.Equal(LuaOperationStatusKind.InvalidResult, architectureStatus.Kind);
		Assert.Equal(CheatEngineArchitecture.Unknown, architecture);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, abiStatus.Kind);
		Assert.Equal(TargetAbi.Unknown, abi);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void RuntimeHostOperations_missing_throwing_and_malformed_globals_keep_distinct_statuses()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		EngineTest.Run(lua, """
		                    function getCEVersion() error('fixture version failure') end
		                    function getSystemArchitecture() return 'x64' end
		                    function getABI() return nil end
		                    """u8);

		LuaOperationStatus versionStatus = RuntimeHostOperations.TryGetCheatEngineVersion(out _);
		LuaOperationStatus architectureStatus = RuntimeHostOperations.TryGetSystemArchitecture(out _);
		LuaOperationStatus abiStatus = RuntimeHostOperations.TryGetTargetAbi(out _);

		Assert.Equal(LuaOperationStatusKind.LuaFailure, versionStatus.Kind);
		Assert.Equal(LuaStatus.RuntimeError, versionStatus.LuaStatus);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, architectureStatus.Kind);
		Assert.Equal(LuaOperationStatusKind.NilResult, abiStatus.Kind);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void integral_float_codes_are_invalid_in_every_api_that_reads_the_same_integer_global()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		FakeHost.InstallCe77HostFacts(lua);
		FakeHost.InstallCe77X64TargetFacts(lua, 4242);
		EngineTest.Run(lua, """
		                    rt_system_architecture = 1.0
		                    rt_operating_system = 0.0
		                    rt_abi = 0.0
		                    """u8);

		LuaOperationStatus architectureStatus = RuntimeHostOperations.TryGetSystemArchitecture(out _);
		LuaOperationStatus operatingSystemStatus = RuntimeHostOperations.TryGetOperatingSystem(out _);
		LuaOperationStatus abiStatus = RuntimeHostOperations.TryGetTargetAbi(out _);
		LuaOperationStatus hostStatus = RuntimeHostOperations.ObserveHost(out _);
		ProcessOperationStatus targetStatus = RuntimeProcessOperations.ObserveTargetArchitecture(out _);

		Assert.Equal(LuaOperationStatusKind.InvalidResult, architectureStatus.Kind);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, operatingSystemStatus.Kind);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, abiStatus.Kind);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, hostStatus.Kind);
		Assert.Equal(ProcessOperationStatusKind.InvalidResult, targetStatus.Kind);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void RuntimeHostOperations_absent_global_reports_unavailability_without_entering_a_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;

		LuaOperationStatus status = RuntimeHostOperations.TryGetCheatEngineVersion(out double version);

		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, status.Kind);
		Assert.Equal(0d, version);
		Assert.Equal(0, lua.Top);
	}

	[Theory]
	[InlineData(true, 8)]
	[InlineData(false, 4)]
	public void ObserveCurrent_target_bitness_preserves_pid_pointer_size_and_does_not_guess_isa(
		bool is64Bit,
		int expectedPointerBytes)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		InstallCurrentProcessGlobals(lua, 42, is64Bit);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveCurrent(out CurrentProcessObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(new TargetProcessId(42), observation.Id);
		Assert.Equal(expectedPointerBytes, observation.PointerSize.Bytes);
		Assert.True(observation.PointerSize.IsKnown);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void ObserveCurrent_no_selected_target_skips_target_bitness_global()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		EngineTest.Run(lua, """
		                    function getOpenedProcessID() return 0 end
		                    function targetIs64Bit() error('must not be called') end
		                    """u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveCurrent(out CurrentProcessObservation observation);

		Assert.Equal(ProcessOperationStatusKind.TargetNotAttached, status.Kind);
		Assert.Equal(default, observation);
		Assert.Equal(0, lua.Top);
	}

	[Theory]
	[InlineData("nil")]
	[InlineData("-1")]
	[InlineData("2147483648")]
	[InlineData("'42'")]
	[InlineData("4242.0")]
	public void ObserveCurrent_malformed_process_identifier_is_not_converted_to_target_absence(string luaResult)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		EngineTest.Run(lua, Encoding.UTF8.GetBytes("function getOpenedProcessID() return " + luaResult + " end"));

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveCurrent(out CurrentProcessObservation observation);

		Assert.Equal(ProcessOperationStatusKind.InvalidResult, status.Kind);
		Assert.Equal(default, observation);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void ObserveCurrent_absent_and_throwing_process_globals_keep_distinct_statuses()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState missingState = new();
		using (HostScope missingScope = new(missingState))
		{
			ProcessOperationStatus missing = RuntimeProcessOperations.ObserveCurrent(out _);

			Assert.Equal(ProcessOperationStatusKind.GlobalUnavailable, missing.Kind);
			Assert.Equal(0, missingScope.State.Top);
		}

		using NativeLuaState throwingState = new();
		using HostScope throwingScope = new(throwingState);
		EngineTest.Run(throwingScope.State, "function getOpenedProcessID() error('fixture process failure') end"u8);

		ProcessOperationStatus throwing = RuntimeProcessOperations.ObserveCurrent(out _);

		Assert.Equal(ProcessOperationStatusKind.ProtectedLuaFailure, throwing.Kind);
		Assert.Equal(LuaStatus.RuntimeError, throwing.LuaStatus);
		Assert.Equal(0, throwingScope.State.Top);
	}

	[Fact]
	public void ObserveCurrent_malformed_target_bitness_is_rejected()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		EngineTest.Run(lua, """
		                    function getOpenedProcessID() return 42 end
		                    function targetIs64Bit() return 'yes' end
		                    """u8);

		ProcessOperationStatus status = RuntimeProcessOperations.ObserveCurrent(out _);

		Assert.Equal(ProcessOperationStatusKind.InvalidResult, status.Kind);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void SelectAndObserve_explicit_pid_requires_the_next_ce_observation_to_match()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState lua = scope.State;
		EngineTest.Run(lua, """
		                    opened = 0
		                    function openProcess(id) opened = id end
		                    function getOpenedProcessID() return opened end
		                    function targetIs64Bit() return true end
		                    """u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.SelectAndObserve(new TargetProcessId(42),
				out CurrentProcessObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(new TargetProcessId(42), observation.Id);
		Assert.Equal(8, observation.PointerSize.Bytes);
		Assert.Equal(0, lua.Top);
	}

	[Fact]
	public void SelectAndObserve_unconfirmed_or_failing_selection_keeps_outcomes_distinct()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState mismatchState = new();
		using (HostScope mismatchScope = new(mismatchState))
		{
			LuaState lua = mismatchScope.State;
			EngineTest.Run(lua, """
			                    function openProcess(_) end
			                    function getOpenedProcessID() return 77 end
			                    function targetIs64Bit() error('must not be called after a mismatched PID') end
			                    """u8);

			ProcessOperationStatus mismatch = RuntimeProcessOperations.SelectAndObserve(new TargetProcessId(42),
				out CurrentProcessObservation observation);

			Assert.Equal(ProcessOperationStatusKind.SelectionNotConfirmed, mismatch.Kind);
			Assert.Equal(default, observation);
			Assert.Equal(0, lua.Top);
		}

		using NativeLuaState failureState = new();
		using HostScope failureScope = new(failureState);
		EngineTest.Run(failureScope.State, "function openProcess(_) error('fixture selection failure') end"u8);

		ProcessOperationStatus failure = RuntimeProcessOperations.SelectAndObserve(new TargetProcessId(42), out _);

		Assert.Equal(ProcessOperationStatusKind.ProtectedLuaFailure, failure.Kind);
		Assert.Equal(LuaStatus.RuntimeError, failure.LuaStatus);
		Assert.Equal(0, failureScope.State.Top);
	}

	[Fact]
	public void SelectAndObserve_rejects_a_default_process_identifier_before_entering_the_lua_runtime()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			RuntimeProcessOperations.SelectAndObserve(default, out _));
	}

	[Fact]
	public void RuntimeProcessOperations_detached_runtime_retains_the_lifecycle_admission_failure()
	{
		LuaRuntime.Detach();

		Assert.Throws<InvalidOperationException>(() => RuntimeProcessOperations.ObserveCurrent(out _));
	}

	[Fact]
	public void RuntimeProcess_capability_identifiers_are_stable_and_distinct()
	{
		Assert.Equal("Process.Current", RuntimeCapabilityId.CurrentProcess.Value);
		Assert.Equal("Process.Selection", RuntimeCapabilityId.ProcessSelection.Value);
		Assert.NotEqual(RuntimeCapabilityId.CurrentProcess, RuntimeCapabilityId.ProcessSelection);
	}

	[Fact]
	[Trait("Qualification", "Q31.a")]
	public void observe_target_architecture_reports_x64_bitness_and_a_narrower_configured_pointer_size_separately()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		// Spike C3 D3a: after setPointerSize(4) on the x64 target, getPointerSize() == 4 while targetIs64Bit() stays true.
		EngineTest.Run(scope.State, "rt_pointer_size = 4"u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(CheatEngineArchitecture.X64, observation.Architecture);
		Assert.Equal(PointerSize.Bit64, observation.Bitness);
		Assert.Equal(4, observation.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit32, observation.ConfiguredPointerSize);
		Assert.True(observation.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32.a")]
	public void observe_target_architecture_with_the_spike_x64_facts_reports_x64_and_eight_byte_pointers()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(new TargetProcessId(4242), observation.ProcessId);
		Assert.Equal(TargetBackend.LocalProcess, observation.Backend);
		Assert.Equal(PointerSize.Bit64, observation.Bitness);
		Assert.True(observation.IsX86Family);
		Assert.False(observation.IsArmFamily);
		Assert.False(observation.IsAndroid);
		Assert.Equal(0, observation.AbiCode);
		Assert.Equal(TargetAbi.Windows, observation.Abi);
		Assert.Equal(8, observation.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit64, observation.ConfiguredPointerSize);
		Assert.Equal(CheatEngineArchitecture.X64, observation.Architecture);
		Assert.False(observation.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32.b")]
	public void observe_target_architecture_with_the_spike_x86_facts_reports_x86_and_four_byte_pointers()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X86TargetFacts(scope.State, 21544);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(CheatEngineArchitecture.X86, observation.Architecture);
		Assert.Equal(PointerSize.Bit32, observation.Bitness);
		Assert.Equal(PointerSize.Bit32, observation.ConfiguredPointerSize);
		Assert.False(observation.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[Trait("Qualification", "Q32.d")]
	[InlineData(false, true, false, CheatEngineArchitecture.Arm32)]
	[InlineData(false, true, true, CheatEngineArchitecture.Arm64)]
	[InlineData(true, true, true, CheatEngineArchitecture.Unknown)]
	[InlineData(false, false, true, CheatEngineArchitecture.Unknown)]
	public void observe_target_architecture_maps_arm32_arm64_and_keeps_contradictory_families_unknown(bool isX86,
		bool isArm, bool is64Bit, CheatEngineArchitecture expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"rt_is_x86 = " + LuaBoolean(isX86) + "\nrt_is_arm = " + LuaBoolean(isArm) + "\nrt_is_64bit = " +
			LuaBoolean(is64Bit)));

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(expected, observation.Architecture);
		Assert.Equal(isX86, observation.IsX86Family);
		Assert.Equal(isArm, observation.IsArmFamily);
		Assert.Equal(is64Bit ? PointerSize.Bit64 : PointerSize.Bit32, observation.Bitness);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32.d")]
	public void observe_target_architecture_keeps_absent_isa_android_abi_and_pointer_probes_unknown_instead_of_false()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, """
		                            function getOpenedProcessID() return 4242 end
		                            function targetIs64Bit() return true end
		                            """u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(TargetBackend.Unknown, observation.Backend);
		Assert.Equal(PointerSize.Bit64, observation.Bitness);
		Assert.Null(observation.IsX86Family);
		Assert.Null(observation.IsArmFamily);
		Assert.Null(observation.IsAndroid);
		Assert.Null(observation.AbiCode);
		Assert.Equal(TargetAbi.Unknown, observation.Abi);
		Assert.Null(observation.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Unknown, observation.ConfiguredPointerSize);
		Assert.Null(observation.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(CheatEngineArchitecture.Unknown, observation.Architecture);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_target_architecture_keeps_an_undocumented_abi_code_without_failing()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, "rt_abi = 7"u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(7, observation.AbiCode);
		Assert.Equal(TargetAbi.Unknown, observation.Abi);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void observe_target_architecture_without_a_selected_target_reads_no_fact()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 0);
		RaiseOnEveryTargetFact(scope.State);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.Equal(ProcessOperationStatusKind.TargetNotAttached, status.Kind);
		Assert.Equal(default, observation);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData("rt_process_id = 5151")]
	[InlineData("rt_process_id = 0")]
	[InlineData("rt_process_id = 4294967295")]
	public void observe_target_architecture_reports_a_pid_change_between_the_bracketing_reads_as_target_changed(
		string change)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"function getPointerSize() " + change + " return rt_pointer_size end"));

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.Equal(ProcessOperationStatusKind.TargetChanged, status.Kind);
		Assert.Equal(default, observation);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_target_architecture_keeps_the_failure_of_the_closing_pid_read()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, """
		                            pid_reads = 0
		                            function getOpenedProcessID()
		                              pid_reads = pid_reads + 1
		                              if pid_reads > 1 then error('fixture closing PID read failure') end
		                              return rt_process_id
		                            end
		                            """u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.Equal(ProcessOperationStatusKind.ProtectedLuaFailure, status.Kind);
		Assert.Equal(LuaStatus.RuntimeError, status.LuaStatus);
		Assert.Equal(default, observation);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.d")]
	public void observe_target_architecture_on_a_ceserver_connection_keeps_the_facts_and_names_the_backend()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, "rt_ceserver = true"u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.True(status.IsSuccess);
		Assert.Equal(TargetBackend.CEServer, observation.Backend);
		Assert.Equal(new TargetProcessId(4242), observation.ProcessId);
		Assert.Equal(CheatEngineArchitecture.X64, observation.Architecture);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.c")]
	public void observe_target_architecture_reports_the_file_as_process_sentinel_without_reading_facts()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, "rt_process_id = 4294967295"u8);
		RaiseOnEveryTargetFact(scope.State);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.Equal(ProcessOperationStatusKind.FileAsProcessTarget, status.Kind);
		Assert.Equal(default, observation);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("isConnectedToCEServer", "error('fixture probe failure')",
		ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData("isConnectedToCEServer", "return nil", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("isConnectedToCEServer", "return 'false'", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("targetIsX86", "error('fixture probe failure')", ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData("targetIsX86", "return nil", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("targetIsArm", "return 0", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("targetIsAndroid", "return nil", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("targetIsAndroid", "error('fixture probe failure')", ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData("getABI", "return 'x'", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("getABI", "return 0.5", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("getPointerSize", "return nil", ProcessOperationStatusKind.InvalidResult)]
	[InlineData("getPointerSize", "error('fixture probe failure')", ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData("targetIs64Bit", "return nil", ProcessOperationStatusKind.InvalidResult)]
	public void raising_or_malformed_optional_probe_fails_the_observation_instead_of_becoming_unknown(string global,
		string body, ProcessOperationStatusKind expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("function " + global + "() " + body + " end"));

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.Equal(expected, status.Kind);
		Assert.Equal(default, observation);
		if (expected == ProcessOperationStatusKind.ProtectedLuaFailure)
		{
			Assert.Equal(LuaStatus.RuntimeError, status.LuaStatus);
		}

		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_target_architecture_requires_the_bitness_probe()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, "targetIs64Bit = nil"u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.Equal(ProcessOperationStatusKind.GlobalUnavailable, status.Kind);
		Assert.Equal(default, observation);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void the_next_observation_after_a_probe_failure_succeeds_on_the_same_state()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, """
		                            rt_raise_android = true
		                            function targetIsAndroid()
		                              if rt_raise_android then error('fixture probe failure') end
		                              return rt_is_android
		                            end
		                            """u8);

		ProcessOperationStatus failed = RuntimeProcessOperations.ObserveTargetArchitecture(out _);
		Assert.Equal(0, scope.State.Top);
		EngineTest.Run(scope.State, "rt_raise_android = false"u8);
		ProcessOperationStatus recovered =
			RuntimeProcessOperations.ObserveTargetArchitecture(out TargetArchitectureObservation observation);

		Assert.Equal(ProcessOperationStatusKind.ProtectedLuaFailure, failed.Kind);
		Assert.True(recovered.IsSuccess);
		Assert.Equal(CheatEngineArchitecture.X64, observation.Architecture);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData(4, ProcessOperationStatusKind.Success)]
	[InlineData(8, ProcessOperationStatusKind.Success)]
	public void configured_pointer_size_of_four_or_eight_is_reported_with_its_raw_value(int configured,
		ProcessOperationStatusKind expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"rt_pointer_size = " + configured.ToString(CultureInfo.InvariantCulture)));

		ProcessOperationStatus status =
			RuntimeProcessOperations.TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize);

		Assert.Equal(expected, status.Kind);
		Assert.Equal(configured, rawBytes);
		Assert.Equal(new PointerSize(configured), pointerSize);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[Trait("Qualification", "Q31")]
	[InlineData("2", 2)]
	[InlineData("0", 0)]
	[InlineData("-1", -1)]
	[InlineData("16", 16)]
	[InlineData("2147483648", 0)]
	[InlineData("4.0", 0)]
	[InlineData("'4'", 0)]
	public void configured_pointer_size_outside_four_and_eight_is_invalid_and_keeps_the_raw_value(string luaValue,
		int expectedRaw)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		// Spike C3 D3b: setPointerSize accepts any integer (2 was stored and read back).
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("rt_pointer_size = " + luaValue));

		ProcessOperationStatus status =
			RuntimeProcessOperations.TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize);

		Assert.Equal(ProcessOperationStatusKind.InvalidResult, status.Kind);
		Assert.Equal(expectedRaw, rawBytes);
		Assert.Equal(PointerSize.Unknown, pointerSize);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void configured_pointer_size_is_not_read_without_a_selected_target()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		// With no target CE 7.7 still answers getPointerSize() == 8 (spike C3 D2); the SDK must not report it.
		FakeHost.InstallCe77X64TargetFacts(scope.State, 0);
		EngineTest.Run(scope.State,
			"function getPointerSize() error('getPointerSize must not be read without a target') end"u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize);

		Assert.Equal(ProcessOperationStatusKind.TargetNotAttached, status.Kind);
		Assert.Equal(0, rawBytes);
		Assert.Equal(PointerSize.Unknown, pointerSize);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void configured_pointer_size_comes_from_get_pointer_size_not_from_the_plugin_process_width()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		Assert.Equal(8, IntPtr.Size);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, "rt_pointer_size = 4"u8);

		ProcessOperationStatus status =
			RuntimeProcessOperations.TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize);

		Assert.True(status.IsSuccess);
		Assert.Equal(4, rawBytes);
		Assert.Equal(PointerSize.Bit32, pointerSize);
		Assert.NotEqual(IntPtr.Size, pointerSize.Bytes);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("targetIs64Bit")]
	[InlineData("targetIsX86")]
	[InlineData("isConnectedToCEServer")]
	[InlineData("getABI")]
	public void configured_pointer_size_reads_only_the_pid_and_get_pointer_size(string otherGlobal)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"function " + otherGlobal + "() error('not part of the configured pointer size read') end"));

		ProcessOperationStatus status = RuntimeProcessOperations.TryGetConfiguredPointerSize(out int rawBytes, out _);

		Assert.True(status.IsSuccess);
		Assert.Equal(8, rawBytes);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("getPointerSize = nil", ProcessOperationStatusKind.GlobalUnavailable)]
	[InlineData("rt_process_id = 4294967295", ProcessOperationStatusKind.FileAsProcessTarget)]
	[InlineData("function getPointerSize() rt_process_id = 77 return 8 end", ProcessOperationStatusKind.TargetChanged)]
	[InlineData("function getPointerSize() error('fixture failure') end",
		ProcessOperationStatusKind.ProtectedLuaFailure)]
	public void configured_pointer_size_keeps_availability_backend_change_and_lua_failures_distinct(string fixture,
		ProcessOperationStatusKind expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(fixture));

		ProcessOperationStatus status =
			RuntimeProcessOperations.TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize);

		Assert.Equal(expected, status.Kind);
		Assert.Equal(0, rawBytes);
		Assert.Equal(PointerSize.Unknown, pointerSize);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.c")]
	public void observe_current_reports_the_file_as_process_sentinel_distinctly_from_a_malformed_pid()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, """
		                            selected = 4294967295
		                            function getOpenedProcessID() return selected end
		                            function targetIs64Bit() error('a file opened as a process has no bitness probe here') end
		                            """u8);

		ProcessOperationStatus sentinel =
			RuntimeProcessOperations.ObserveCurrent(out CurrentProcessObservation sentinelObservation);
		EngineTest.Run(scope.State, "selected = 4294967294"u8);
		ProcessOperationStatus malformed = RuntimeProcessOperations.ObserveCurrent(out _);

		Assert.Equal(ProcessOperationStatusKind.FileAsProcessTarget, sentinel.Kind);
		Assert.Equal(default, sentinelObservation);
		Assert.Equal(ProcessOperationStatusKind.InvalidResult, malformed.Kind);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q31.a")]
	public void observe_current_keeps_bitness_as_its_pointer_size_while_the_configured_size_differs()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 4242);
		EngineTest.Run(scope.State, "rt_pointer_size = 4"u8);

		ProcessOperationStatus status = RuntimeProcessOperations.ObserveCurrent(out CurrentProcessObservation current);

		Assert.True(status.IsSuccess);
		Assert.Equal(PointerSize.Bit64, current.PointerSize);
		Assert.Equal(0, scope.State.Top);
	}

	private static void RaiseOnEveryTargetFact(LuaState state)
	{
		EngineTest.Run(state, """
		                      local function forbidden(name)
		                        return function() error(name .. ' must not be read for this selection') end
		                      end
		                      isConnectedToCEServer = forbidden('isConnectedToCEServer')
		                      targetIs64Bit = forbidden('targetIs64Bit')
		                      targetIsX86 = forbidden('targetIsX86')
		                      targetIsArm = forbidden('targetIsArm')
		                      targetIsAndroid = forbidden('targetIsAndroid')
		                      getABI = forbidden('getABI')
		                      getPointerSize = forbidden('getPointerSize')
		                      """u8);
	}

	private static void InstallCurrentProcessGlobals(LuaState state, int processId, bool is64Bit)
	{
		string source = "function getOpenedProcessID() return " + processId + " end\n" +
						"function targetIs64Bit() return " + LuaBoolean(is64Bit) + " end";
		EngineTest.Run(state, Encoding.UTF8.GetBytes(source));
	}

	private static string LuaBoolean(bool value)
	{
		return value ? "true" : "false";
	}
}
