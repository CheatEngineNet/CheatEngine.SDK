using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Registration;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Callbacks;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>
///     The 2.0 decision for <c>resetLuaState</c> (WI-3, O2): no public reset API; a reset the SDK was not told about
///     is detected deterministically, refuses old owners, never releases a reference into the replacement registry
///     (A08-22), and reports one diagnostic. Uses a second <see cref="NativeLuaState" /> as the "new VM" and
///     <see cref="HostDouble.SetStateForCurrentThread" /> to simulate the host swapping the provider's answer without
///     going through <see cref="LuaRuntime.BeginStateReset" />.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class LuaExternalResetDetectionTests
{
	[Fact]
	[Trait("Qualification", "Q18")]
	public void An_external_state_replacement_is_detected_on_the_next_provider_acquisition()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);
		using NativeLuaState replacement = new();
		Assert.False(LuaRuntime.ExternalStateResetDetected);
		HostDouble.SetStateForCurrentThread(replacement.Pointer);

		LuaAdmissionStatus status = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
		operation.Dispose();

		Assert.Equal(LuaAdmissionStatus.ExternalStateReset, status);
		Assert.True(LuaRuntime.ExternalStateResetDetected);
	}

	[Fact]
	[Trait("Qualification", "Q18")]
	public void After_an_external_reset_every_admission_path_reports_ExternalStateReset_until_detach()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using NativeLuaState replacement = new();
		HostDouble.SetStateForCurrentThread(replacement.Pointer);
		TriggerExternalReset();

		Assert.True(LuaRuntime.ExternalStateResetDetected);

		LuaAdmissionStatus status = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation provider);
		provider.Dispose();
		Assert.Equal(LuaAdmissionStatus.ExternalStateReset, status);

		Assert.False(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation viaTry));
		viaTry.Dispose();

		InvalidOperationException
			thrown = Assert.Throws<InvalidOperationException>(() => LuaRuntime.AcquireOperation());
		Assert.Contains("replaced its Lua state", thrown.Message, StringComparison.Ordinal);

		Assert.Throws<InvalidOperationException>(() => LuaRuntime.AcquireOperation(main));

		Assert.False(LuaRuntime.TryEnterCallbackOperation(out LuaRuntimeOperation callback));
		callback.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q18")]
	public void External_reset_detection_advances_the_state_generation_so_old_references_are_stale()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		main.PushString("before reset"u8);
		LuaRef reference = main.CreateRef();
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;
		using NativeLuaState replacement = new();
		HostDouble.SetStateForCurrentThread(replacement.Pointer);

		TriggerExternalReset();

		LuaStateIdentity after = LuaRuntime.CurrentStateIdentity;
		Assert.Equal(before.AttachEpoch, after.AttachEpoch);
		Assert.Equal(before.StateGeneration + 1, after.StateGeneration);
		Assert.True(reference.IsResolved);
		Assert.False(reference.IsCurrent);
		reference.Release(default);
	}

	[Fact]
	[Trait("Qualification", "Q18")]
	public void A_reference_from_before_an_external_reset_is_never_released_into_the_replacement_registry()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		main.PushString("before reset"u8);
		LuaRef reference = main.CreateRef();
		using NativeLuaState replacement = new();
		LuaState replacementView = new(replacement.L);
		int replacementTopBefore = replacementView.Top;
		HostDouble.SetStateForCurrentThread(replacement.Pointer);

		TriggerExternalReset();

		// A stale reference releases as a no-op: it never touches the replacement state's stack or registry.
		reference.Release(replacementView);
		Assert.Equal(replacementTopBefore, replacementView.Top);
	}

	[Fact]
	[Trait("Qualification", "Q18")]
	public void Detach_after_an_external_reset_abandons_callbacks_without_calling_Lua_on_the_replacement_state()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Assert.True(LuaCallback.TryCreate(main, Thunks.Count, new Counter(), out LuaCallback<Counter>? callback).IsOk);
		Assert.NotNull(callback);
		using NativeLuaState replacement = new();
		LuaState replacementView = new(replacement.L);
		int replacementTopBefore = replacementView.Top;
		HostDouble.SetStateForCurrentThread(replacement.Pointer);

		TriggerExternalReset();
		Assert.True(LuaRuntime.ExternalStateResetDetected);

		LuaRuntime.Detach();

		Assert.True(callback.IsReleased);
		Assert.Equal(0, LuaCallbackRegistry.Count);
		Assert.Equal(replacementTopBefore, replacementView.Top);
	}

	[Fact]
	[Trait("Qualification", "Q18")]
	public void Reattach_after_an_external_reset_restamps_and_admits_again()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);
		using NativeLuaState replacement = new();
		HostDouble.SetStateForCurrentThread(replacement.Pointer);
		TriggerExternalReset();
		Assert.True(LuaRuntime.ExternalStateResetDetected);
		LuaRuntime.Detach();

		LuaHostBinding binding = HostDouble.CreateBinding(replacement.L);
		LuaRuntime.Attach(in binding);
		try
		{
			Assert.False(LuaRuntime.ExternalStateResetDetected);
			Assert.True(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation));
			operation.Dispose();
		}
		finally
		{
			LuaRuntime.Detach();
		}
	}

	[Fact]
	[Trait("Qualification", "Q18")]
	public void The_external_reset_diagnostic_is_reported_once_per_attachment()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);
		using NativeLuaState replacement = new();
		int raisedCount = 0;
		LuaRuntimeDiagnostic? lastKind = null;
		LuaRuntime.DiagnosticObserver = kind =>
		{
			raisedCount++;
			lastKind = kind;
		};

		try
		{
			HostDouble.SetStateForCurrentThread(replacement.Pointer);
			for (int i = 0; i < 3; i++)
			{
				TriggerExternalReset();
			}

			Assert.Equal(1, raisedCount);
			Assert.Equal(LuaRuntimeDiagnostic.ExternalStateReset, lastKind);
		}
		finally
		{
			LuaRuntime.DiagnosticObserver = null;
		}
	}

	[Fact]
	[Trait("Qualification", "Q17")]
	public void The_supported_reset_restamps_the_replacement_state_and_is_not_reported_as_external()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		using RuntimeScope scope = new(state);
		using NativeLuaState replacement = new();

		using (LuaRuntime.BeginStateReset())
		{
			HostDouble.SetStateForCurrentThread(replacement.Pointer);
		}

		Assert.False(LuaRuntime.ExternalStateResetDetected);
		Assert.True(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation));
		operation.Dispose();
		Assert.False(LuaRuntime.ExternalStateResetDetected);
	}

	[Fact]
	[Trait("Qualification", "Q17")]
	public void
		A_state_replacement_during_a_registration_transaction_leaves_the_lease_stale_and_the_new_state_untouched()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState main = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using NativeLuaState replacement = new();
		LuaState replacementView = new(replacement.L);
		LuaRegistrationEntry[] entries = [new("sdk013_reset_registration", Thunks.Add)];
		LuaRegistrationResult result = LuaRegistrationSet.Register(main, entries);
		Assert.True(result.IsSuccess);
		LuaRegistrationLease lease = result.Lease!;
		int replacementTopBefore = replacementView.Top;

		using (LuaRuntime.BeginStateReset())
		{
			HostDouble.SetStateForCurrentThread(replacement.Pointer);
		}

		LuaRegistrationReleaseOutcome outcome = lease.ReleaseWithOutcome();
		Assert.Equal(LuaRegistrationReleaseKind.Stale, outcome.Kind);
		Assert.Equal(replacementTopBefore, replacementView.Top);
	}

	[Fact]
	public void Sdk_reference_tables_use_private_registry_keys_distinct_from_the_runtime_stamp_and_host_references()
	{
		Assert.NotEqual(LuaReferences.Key, LuaRuntime.StampKeyForTests);
		Assert.NotEqual(0, LuaReferences.Key);
		Assert.NotEqual(0, LuaRuntime.StampKeyForTests);
	}

	// Simulates the host replacing the Lua state behind the SDK's back: the provider now answers with the
	// replacement state's pointer, but nothing called LuaRuntime.BeginStateReset first. One admitted acquisition
	// attempt is enough to trigger detection.
	private static void TriggerExternalReset()
	{
		LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
		operation.Dispose();
	}
}
