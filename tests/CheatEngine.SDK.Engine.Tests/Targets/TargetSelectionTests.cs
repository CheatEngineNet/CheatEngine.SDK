using System.Text;

using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Targets;

/// <summary>Fixture-level observations for the SDK target-selection and incarnation contract.</summary>
[Trait("Category", "NativeLua")]
public sealed class TargetSelectionTests
{
	[Fact]
	public void ObserveCurrent_with_the_current_process_id_returns_a_qualified_incarnation()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallOpenedProcessId(scope.State, Environment.ProcessId);

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();

		Assert.Equal(TargetSelectionObservationStatus.CurrentTargetQualified, observation.Status);
		Assert.Equal(Environment.ProcessId, observation.SelectedProcessId);
		Assert.True(observation.IsQualified);
		Assert.True(observation.Incarnation.HasValue);
		Assert.Equal(Environment.ProcessId, observation.Incarnation.Value.ProcessId);
		Assert.Equal(TargetIdentityEvidence.CheatEngineSelectedProcessId | TargetIdentityEvidence.LocalBackendConfirmed |
			TargetIdentityEvidence.LocalProcessStartTime, observation.Evidence);
		Assert.Equal(TargetBackend.LocalProcess, observation.Backend);
		Assert.True(TargetSelection.ValidateCurrent(observation.Incarnation.Value).IsCurrent);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void ObserveCurrent_with_no_Cheat_Engine_target_reports_no_target_without_inventing_an_incarnation()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallOpenedProcessId(scope.State, 0);

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();

		Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, observation.Status);
		Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
		Assert.Null(observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.False(observation.IsQualified);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void ObserveCurrent_when_the_selection_global_is_unavailable_preserves_that_unavailable_fact()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		TargetProcessIncarnation expected = new(4101, 1001);

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();
		TargetIdentityCheck check = TargetSelection.ValidateCurrent(expected);

		Assert.Equal(TargetSelectionObservationStatus.GlobalUnavailable, observation.Status);
		Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
		Assert.Null(observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.Equal(TargetIdentityCheckKind.GlobalUnavailable, check.Kind);
		Assert.False(check.IsCurrent);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void ObserveCurrent_when_the_selection_callback_raises_preserves_the_Lua_failure_fact()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, "function getOpenedProcessID() error('fixture target selection failure') end"u8);

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();

		Assert.Equal(TargetSelectionObservationStatus.LuaFailure, observation.Status);
		Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
		Assert.Null(observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.False(observation.IsQualified);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("function getOpenedProcessID() return true end")]
	[InlineData("function getOpenedProcessID() return -1 end")]
	[InlineData("function getOpenedProcessID() return '42' end")]
	[InlineData("function getOpenedProcessID() return 4294967294 end")]
	public void ObserveCurrent_when_the_selection_callback_returns_an_invalid_PID_reports_an_invalid_result(
		string fixture)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(fixture));

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();

		Assert.Equal(TargetSelectionObservationStatus.InvalidResult, observation.Status);
		Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
		Assert.Null(observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.False(observation.IsQualified);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void
		ObserveCurrent_when_the_selected_PID_cannot_be_locally_qualified_keeps_the_PID_without_granting_authority()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallOpenedProcessId(scope.State, int.MaxValue);

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();

		Assert.Equal(TargetSelectionObservationStatus.CurrentTargetUnqualified, observation.Status);
		Assert.Equal(TargetIdentityEvidence.CheatEngineSelectedProcessId | TargetIdentityEvidence.LocalBackendConfirmed,
			observation.Evidence);
		Assert.Equal(TargetBackend.LocalProcess, observation.Backend);
		Assert.Equal(int.MaxValue, observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.False(observation.IsQualified);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void ValidateCurrent_distinguishes_a_different_PID_from_reuse_of_the_same_PID()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallOpenedProcessId(scope.State, Environment.ProcessId);
		TargetProcessIncarnation changedTarget = new(Environment.ProcessId + 1, 1001);
		TargetProcessIncarnation reusedPid = new(Environment.ProcessId, 1);

		TargetIdentityCheck changed = TargetSelection.ValidateCurrent(changedTarget);
		TargetIdentityCheck reused = TargetSelection.ValidateCurrent(reusedPid);

		Assert.Equal(TargetIdentityCheckKind.TargetChanged, changed.Kind);
		Assert.Equal(Environment.ProcessId, changed.Observed.SelectedProcessId);
		Assert.Equal(TargetIdentityCheckKind.ProcessReused, reused.Kind);
		Assert.Equal(Environment.ProcessId, reused.Observed.SelectedProcessId);
		Assert.False(changed.IsCurrent);
		Assert.False(reused.IsCurrent);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Default_identity_check_is_not_current()
	{
		TargetIdentityCheck check = default;

		Assert.Equal(TargetIdentityCheckKind.Unspecified, check.Kind);
		Assert.False(check.IsCurrent);
	}

	[Fact]
	public void Default_selection_observation_is_not_qualified()
	{
		TargetSelectionObservation observation = default;

		Assert.Equal(TargetSelectionObservationStatus.Unspecified, observation.Status);
		Assert.False(observation.IsQualified);
	}

	[Fact]
	public void target_selection_emits_no_local_start_time_evidence_when_connected_to_ceserver()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		// The test process's own PID would qualify if a local lookup ran.
		InstallOpenedProcessId(scope.State, Environment.ProcessId);
		EngineTest.Run(scope.State, "function isConnectedToCEServer() return true end"u8);

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();

		Assert.Equal(TargetSelectionObservationStatus.CurrentTargetRemoteBackend, observation.Status);
		Assert.Equal(TargetBackend.CEServer, observation.Backend);
		Assert.Equal(Environment.ProcessId, observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.False(observation.IsQualified);
		Assert.Equal(TargetIdentityEvidence.CheatEngineSelectedProcessId, observation.Evidence);
		Assert.False(observation.Evidence.HasFlag(TargetIdentityEvidence.LocalProcessStartTime));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void target_selection_emits_no_local_start_time_evidence_when_the_ceserver_probe_is_unavailable()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"function getOpenedProcessID() return " + Environment.ProcessId + " end"));

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();
		TargetIdentityCheck check =
			TargetSelection.ValidateCurrent(new TargetProcessIncarnation(Environment.ProcessId, 1001));

		Assert.Equal(TargetSelectionObservationStatus.CurrentTargetBackendUnknown, observation.Status);
		Assert.Equal(TargetBackend.Unknown, observation.Backend);
		Assert.Equal(Environment.ProcessId, observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.False(observation.IsQualified);
		Assert.Equal(TargetIdentityEvidence.CheatEngineSelectedProcessId, observation.Evidence);
		Assert.Equal(TargetIdentityCheckKind.BackendUnknown, check.Kind);
		Assert.False(check.IsCurrent);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void ceserver_target_yields_no_local_incarnation_proof_and_validate_current_refuses_it()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallOpenedProcessId(scope.State, Environment.ProcessId);
		EngineTest.Run(scope.State, """
		                            ceserver_connected = false
		                            function isConnectedToCEServer() return ceserver_connected end
		                            """u8);
		TargetSelectionObservation local = TargetSelection.ObserveCurrent();
		Assert.True(local.IsQualified);
		TargetProcessIncarnation expected = local.Incarnation.GetValueOrDefault();

		// Cheat Engine now serves the same PID through CEServer: the local creation time no longer describes it.
		EngineTest.Run(scope.State, "ceserver_connected = true"u8);
		TargetIdentityCheck check = TargetSelection.ValidateCurrent(expected);
		TargetReleaseOutcome refused = TargetReleaseOutcome.Refused(check);

		Assert.Equal(TargetIdentityCheckKind.RemoteBackend, check.Kind);
		Assert.False(check.IsCurrent);
		Assert.Equal(TargetBackend.CEServer, check.Observed.Backend);
		Assert.Null(check.Observed.Incarnation);
		Assert.Equal(TargetReleaseStatus.RefusedIdentityUnavailable, refused.Status);
		Assert.True(refused.RequiresManualRecovery);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void file_as_process_target_is_refused_without_a_bcl_process_lookup()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, """
		                            function getOpenedProcessID() return 4294967295 end
		                            function isConnectedToCEServer() error('a file opened as a process needs no backend probe') end
		                            """u8);

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();
		TargetIdentityCheck check =
			TargetSelection.ValidateCurrent(new TargetProcessIncarnation(Environment.ProcessId, 1001));

		Assert.Equal(TargetSelectionObservationStatus.CurrentTargetFileAsProcess, observation.Status);
		Assert.Equal(TargetBackend.FileAsProcess, observation.Backend);
		Assert.Null(observation.SelectedProcessId);
		Assert.Null(observation.Incarnation);
		Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
		Assert.False(observation.IsQualified);
		Assert.Equal(TargetIdentityCheckKind.FileAsProcess, check.Kind);
		Assert.False(check.IsCurrent);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("error('fixture backend probe failure')", TargetSelectionObservationStatus.LuaFailure,
		TargetIdentityCheckKind.LuaFailure)]
	[InlineData("return nil", TargetSelectionObservationStatus.InvalidResult, TargetIdentityCheckKind.InvalidResult)]
	[InlineData("return 'false'", TargetSelectionObservationStatus.InvalidResult,
		TargetIdentityCheckKind.InvalidResult)]
	[InlineData("return 0", TargetSelectionObservationStatus.InvalidResult, TargetIdentityCheckKind.InvalidResult)]
	public void raising_or_malformed_ceserver_probe_keeps_the_lua_failure_or_invalid_result(string body,
		TargetSelectionObservationStatus expectedStatus, TargetIdentityCheckKind expectedCheck)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallOpenedProcessId(scope.State, Environment.ProcessId);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("function isConnectedToCEServer() " + body + " end"));

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();
		TargetIdentityCheck check =
			TargetSelection.ValidateCurrent(new TargetProcessIncarnation(Environment.ProcessId, 1001));

		Assert.Equal(expectedStatus, observation.Status);
		Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
		Assert.Null(observation.Incarnation);
		Assert.False(observation.IsQualified);
		Assert.Equal(expectedCheck, check.Kind);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void the_next_target_observation_after_a_ceserver_probe_failure_succeeds()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallOpenedProcessId(scope.State, Environment.ProcessId);
		EngineTest.Run(scope.State, """
		                            raise_backend_probe = true
		                            function isConnectedToCEServer()
		                              if raise_backend_probe then error('fixture backend probe failure') end
		                              return false
		                            end
		                            """u8);

		TargetSelectionObservation failed = TargetSelection.ObserveCurrent();
		Assert.Equal(0, scope.State.Top);
		EngineTest.Run(scope.State, "raise_backend_probe = false"u8);
		TargetSelectionObservation recovered = TargetSelection.ObserveCurrent();

		Assert.Equal(TargetSelectionObservationStatus.LuaFailure, failed.Status);
		Assert.True(recovered.IsQualified);
		Assert.Equal(TargetBackend.LocalProcess, recovered.Backend);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observations_that_differ_only_by_backend_are_not_equal()
	{
		TargetSelectionObservation remote = TargetSelectionObservation.RemoteBackend(4242);
		TargetSelectionObservation unknown = TargetSelectionObservation.BackendUnknown(4242);

		Assert.NotEqual(remote, unknown);
		Assert.Equal(remote.SelectedProcessId, unknown.SelectedProcessId);
		Assert.Equal(remote.Evidence, unknown.Evidence);
		Assert.Equal(TargetBackend.Unknown, default(TargetSelectionObservation).Backend);
	}

	[Fact]
	public void target_enum_members_are_appended_with_pinned_values()
	{
		Assert.Equal(7, (byte) TargetSelectionObservationStatus.CurrentTargetRemoteBackend);
		Assert.Equal(8, (byte) TargetSelectionObservationStatus.CurrentTargetFileAsProcess);
		Assert.Equal(9, (byte) TargetSelectionObservationStatus.CurrentTargetBackendUnknown);
		Assert.Equal(9, (byte) TargetIdentityCheckKind.RemoteBackend);
		Assert.Equal(10, (byte) TargetIdentityCheckKind.FileAsProcess);
		Assert.Equal(11, (byte) TargetIdentityCheckKind.BackendUnknown);
		Assert.Equal(4, (byte) TargetIdentityEvidence.LocalBackendConfirmed);
		Assert.Equal(6, (byte) TargetSelectionObservationStatus.InvalidResult);
		Assert.Equal(8, (byte) TargetIdentityCheckKind.InvalidResult);
		Assert.Equal(2, (byte) TargetIdentityEvidence.LocalProcessStartTime);
	}

	private static void InstallOpenedProcessId(LuaState state, int processId)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes("function getOpenedProcessID() return " + processId + " end"));
		EngineTest.Run(state, FakeHost.LocalTargetBackendChunk);
	}
}
