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
        var lua = scope.State;
        EngineTest.Run(lua, """
                            function getCEVersion() return 7.7 end
                            function getSystemArchitecture() return 1 end
                            function getABI() return 0 end
                            """u8);

        LuaOperationStatus versionStatus = RuntimeHostOperations.TryGetCheatEngineVersion(out var version);
        LuaOperationStatus architectureStatus = RuntimeHostOperations.TryGetSystemArchitecture(out var architecture);
        LuaOperationStatus abiStatus = RuntimeHostOperations.TryGetTargetAbi(out var abi);

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
        var lua = scope.State;
        EngineTest.Run(lua, """
                            function getSystemArchitecture() return 99 end
                            function getABI() return 99 end
                            """u8);

        LuaOperationStatus architectureStatus = RuntimeHostOperations.TryGetSystemArchitecture(out var architecture);
        LuaOperationStatus abiStatus = RuntimeHostOperations.TryGetTargetAbi(out var abi);

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
        var lua = scope.State;
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
    public void RuntimeHostOperations_absent_global_reports_unavailability_without_entering_a_call()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var lua = scope.State;

        LuaOperationStatus status = RuntimeHostOperations.TryGetCheatEngineVersion(out var version);

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
        var lua = scope.State;
        InstallCurrentProcessGlobals(lua, 42, is64Bit);

        ProcessOperationStatus status = RuntimeProcessOperations.ObserveCurrent(out var observation);

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
        var lua = scope.State;
        EngineTest.Run(lua, """
                            function getOpenedProcessID() return 0 end
                            function targetIs64Bit() error('must not be called') end
                            """u8);

        ProcessOperationStatus status = RuntimeProcessOperations.ObserveCurrent(out var observation);

        Assert.Equal(ProcessOperationStatusKind.TargetNotAttached, status.Kind);
        Assert.Equal(default, observation);
        Assert.Equal(0, lua.Top);
    }

    [Theory]
    [InlineData("nil")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("'42'")]
    public void ObserveCurrent_malformed_process_identifier_is_not_converted_to_target_absence(string luaResult)
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var lua = scope.State;
        EngineTest.Run(lua, Encoding.UTF8.GetBytes("function getOpenedProcessID() return " + luaResult + " end"));

        ProcessOperationStatus status = RuntimeProcessOperations.ObserveCurrent(out var observation);

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
        var lua = scope.State;
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
        var lua = scope.State;
        EngineTest.Run(lua, """
                            opened = 0
                            function openProcess(id) opened = id end
                            function getOpenedProcessID() return opened end
                            function targetIs64Bit() return true end
                            """u8);

        ProcessOperationStatus status = RuntimeProcessOperations.SelectAndObserve(new TargetProcessId(42), out var observation);

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
            var lua = mismatchScope.State;
            EngineTest.Run(lua, """
                                function openProcess(_) end
                                function getOpenedProcessID() return 77 end
                                function targetIs64Bit() error('must not be called after a mismatched PID') end
                                """u8);

            ProcessOperationStatus mismatch = RuntimeProcessOperations.SelectAndObserve(new TargetProcessId(42), out var observation);

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
