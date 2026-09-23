using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     Applies an Auto Assembler script against the current Cheat Engine target and creates the corresponding owned
///     disable lease; checks a script without applying it.
/// </summary>
/// <remarks>
///     <para>
///         This is deliberately the low-level SDK escape hatch. It passes only the script to CE's <c>autoAssemble</c>
///         global and never supplies CE's <c>targetself</c> argument. A caller that reaches this API is responsible for
///         the script's content; higher-level APIs should expose typed, capability-gated patches rather than arbitrary
///         script execution. The SDK captures a qualified target incarnation before it applies a script, validates it
///         again in the same Lua operation right after the effect, and validates it before disabling the resulting
///         owner; it never supplies <c>targetself</c>, opens, or selects a process. CE exposes no inspected primitive
///         that binds that observation atomically to the following ambient-target call, so an external selection
///         transition in that interval is not live-qualified as safe.
///     </para>
///     <para>
///         <c>autoAssemble</c> is called with three results (success, disable information or error detail, compilation
///         warnings) so that no result Cheat Engine returns is dropped. <see cref="TryApplyWithOutcome(string, out AutoAssemblerPatch?)" />
///         reports every result as an <see cref="AutoAssemblerApplyOutcome" />; the older <see cref="TryApply" /> and
///         <see cref="Apply" /> keep their Boolean and exception contracts. Both globals are resolved through the SDK's
///         cached, protected global push.
///     </para>
/// </remarks>
public static class AutoAssemblerPatcher
{
	private const string ApplyOperation = "AutoAssemblerApply";
	private const string DisableOperation = "AutoAssemblerDisable";

	private static readonly LuaRef SAutoAssemble = new();
	private static readonly LuaRef SAutoAssembleCheck = new();

	/// <summary>
	///     Applies <paramref name="script" /> and returns the sole owner of the resulting CE disable information.
	/// </summary>
	/// <param name="script">The complete Auto Assembler script, including its <c>[ENABLE]</c> and <c>[DISABLE]</c> sections.</param>
	/// <returns>An active patch which must be released or disposed before the plugin is disabled.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is empty or white-space only.</exception>
	/// <exception cref="EngineOperationFailedException">Cheat Engine rejected the Auto Assembler script.</exception>
	/// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or not a function.</exception>
	/// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
	/// <exception cref="EngineMarshallingException">CE returned a success result without a disable-info table.</exception>
	/// <exception cref="EngineTargetIdentityException">The selected target cannot be qualified as an incarnation.</exception>
	/// <exception cref="EngineResourceHandoffException">
	///     CE accepted the script but disable-info tracking or patch publication failed; the exception reports its one
	///     target-qualified disable attempt.
	/// </exception>
	[RequiresPluginEnabled]
	public static AutoAssemblerPatch Apply(string script)
	{
		if (TryApply(script, out AutoAssemblerPatch? patch))
		{
			return patch;
		}

		throw new EngineOperationFailedException(ApplyOperation);
	}

	/// <summary>
	///     Attempts to apply <paramref name="script" /> and transfers CE's disable-info table to
	///     <paramref name="patch" /> on success.
	/// </summary>
	/// <param name="script">The complete Auto Assembler script, including its <c>[ENABLE]</c> and <c>[DISABLE]</c> sections.</param>
	/// <param name="patch">The active patch owner on success; otherwise <see langword="null" />.</param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the script.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is empty or white-space only.</exception>
	/// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or not a function.</exception>
	/// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
	/// <exception cref="EngineMarshallingException">CE returned a success result without a disable-info table.</exception>
	/// <exception cref="EngineTargetIdentityException">The selected target cannot be qualified as an incarnation.</exception>
	/// <exception cref="EngineResourceHandoffException">
	///     CE accepted the script but disable-info tracking or patch publication failed; the exception reports its one
	///     target-qualified disable attempt.
	/// </exception>
	/// <remarks>
	///     A <see langword="false" /> result does not prove that nothing changed; use
	///     <see cref="TryApplyWithOutcome(string, out AutoAssemblerPatch?)" /> for the effect state and the diagnostic.
	/// </remarks>
	[RequiresPluginEnabled]
	public static bool TryApply(string script, [NotNullWhen(true)] out AutoAssemblerPatch? patch)
	{
		return TryApplyCore(script, out patch, CreateDisableInfo, CreatePatch);
	}

	/// <summary>
	///     Applies <paramref name="script" /> with <see cref="AutoAssemblerOptions.Default" /> and reports a factual
	///     outcome instead of a Boolean or an Engine exception.
	/// </summary>
	/// <param name="script">The complete Auto Assembler script, including its <c>[ENABLE]</c> and <c>[DISABLE]</c> sections.</param>
	/// <param name="patch">
	///     The patch owner when <see cref="AutoAssemblerApplyOutcome.HasPatch" /> is <see langword="true" />; otherwise
	///     <see langword="null" />.
	/// </param>
	/// <returns>The outcome of the one activation attempt.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is empty or white-space only.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static AutoAssemblerApplyOutcome TryApplyWithOutcome(string script, out AutoAssemblerPatch? patch)
	{
		return TryApplyWithOutcome(script, AutoAssemblerOptions.Default, out patch);
	}

	/// <summary>
	///     Applies <paramref name="script" /> and reports a factual outcome: the category, the effect state, the opt-in
	///     bounded host text and warnings, the target observations, the disable-info snapshot, and the compensation made
	///     when a patch could not be published. No Engine exception crosses this method.
	/// </summary>
	/// <param name="script">The complete Auto Assembler script, including its <c>[ENABLE]</c> and <c>[DISABLE]</c> sections.</param>
	/// <param name="options">The host-text opt-in and the snapshot bounds.</param>
	/// <param name="patch">
	///     The patch owner when <see cref="AutoAssemblerApplyOutcome.HasPatch" /> is <see langword="true" />; otherwise
	///     <see langword="null" />.
	/// </param>
	/// <returns>The outcome of the one activation attempt.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is empty or white-space only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A bound of <paramref name="options" /> is out of range.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     When Cheat Engine applied the script and the target no longer matched afterwards, the outcome is
	///     <see cref="AutoAssemblerApplyOutcomeKind.AppliedTargetChanged" /> and the patch is still published with its
	///     original incarnation: its release refuses the other target instead of losing the only disable token.
	/// </remarks>
	[RequiresPluginEnabled]
	public static AutoAssemblerApplyOutcome TryApplyWithOutcome(string script, AutoAssemblerOptions options,
		out AutoAssemblerPatch? patch)
	{
		return ApplyCore(script, options, CreateDisableInfo, CreatePatch, false, out patch, out _);
	}

	/// <summary>
	///     Checks <paramref name="script" /> with <c>autoAssembleCheck</c> and <see cref="AutoAssemblerOptions.Default" />.
	/// </summary>
	/// <param name="script">The Auto Assembler script to check.</param>
	/// <param name="enable"><see langword="true" /> to check the <c>[ENABLE]</c> section, <see langword="false" /> for <c>[DISABLE]</c>.</param>
	/// <returns>The outcome of the check.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is empty or white-space only.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static AutoAssemblerCheckOutcome TryCheck(string script, bool enable)
	{
		return TryCheck(script, enable, AutoAssemblerOptions.Default);
	}

	/// <summary>
	///     Checks <paramref name="script" /> with <c>autoAssembleCheck</c>, passing exactly the script and the enable flag
	///     (never <c>targetself</c>).
	/// </summary>
	/// <param name="script">The Auto Assembler script to check.</param>
	/// <param name="enable"><see langword="true" /> to check the <c>[ENABLE]</c> section, <see langword="false" /> for <c>[DISABLE]</c>.</param>
	/// <param name="options">The host-text opt-in and bound.</param>
	/// <returns>The outcome of the check.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is empty or white-space only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A bound of <paramref name="options" /> is out of range.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     A syntax check is not proof that the activation will succeed: targets or symbols may change before it, and
	///     allocations or injections are not attempted. A check never creates an owner and never replaces handling the
	///     result of the activation itself.
	/// </remarks>
	[RequiresPluginEnabled]
	public static AutoAssemblerCheckOutcome TryCheck(string script, bool enable, AutoAssemblerOptions options)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(script);
		ArgumentNullException.ThrowIfNull(options);
		options.Validate(nameof(options));

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaGlobalPushOutcome global =
			LuaGlobalFunctions.TryPushWithOutcome(state, SAutoAssembleCheck, "autoAssembleCheck"u8);
		if (!global.IsSuccess)
		{
			return global.Status == LuaGlobalPushStatus.Unavailable
				? new AutoAssemblerCheckOutcome(AutoAssemblerCheckOutcomeKind.GlobalUnavailable, LuaStatus.Ok, null,
					false)
				: new AutoAssemblerCheckOutcome(AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure,
					ToFailureStatus(global.LuaStatus), null, false);
		}

		StringMarshaller.Push(state, script);
		state.PushBoolean(enable);
		LuaStatus status = state.TryCall(2, 2);
		if (!status.IsOk)
		{
			return new AutoAssemblerCheckOutcome(AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure, status, null,
				false);
		}

		if (state.TypeOf(-2) != LuaType.Boolean)
		{
			return new AutoAssemblerCheckOutcome(AutoAssemblerCheckOutcomeKind.InvalidResult, LuaStatus.Ok, null,
				false);
		}

		if (state.ToBoolean(-2))
		{
			return new AutoAssemblerCheckOutcome(AutoAssemblerCheckOutcomeKind.Accepted, LuaStatus.Ok, null, false);
		}

		AutoAssemblerHostTextCopy text =
			AutoAssemblerHostText.Copy(state, -1, options.CaptureHostText, options.MaxHostTextBytes);
		return new AutoAssemblerCheckOutcome(AutoAssemblerCheckOutcomeKind.Rejected, LuaStatus.Ok, text.Text,
			text.Truncated);
	}

	// The seams are internal test infrastructure. A caller cannot select tracking or ownership behavior; they let the
	// SDK prove that every exception between a successful apply and publication retains one compensation authority.
	// This is the legacy Boolean/exception projection of ApplyCore.
	internal static bool TryApplyCore(string script, [NotNullWhen(true)] out AutoAssemblerPatch? patch,
		AutoAssemblerDisableInfoTracker disableInfoTracker, AutoAssemblerPatchFactory patchFactory)
	{
		AutoAssemblerApplyOutcome outcome = ApplyCore(script, AutoAssemblerOptions.Default, disableInfoTracker,
			patchFactory, true, out patch, out Exception? cause);
		switch (outcome.Kind)
		{
			case AutoAssemblerApplyOutcomeKind.Applied or AutoAssemblerApplyOutcomeKind.AppliedTargetChanged:
				return patch is not null;
			case AutoAssemblerApplyOutcomeKind.Rejected:
				patch = null;
				return false;
			case AutoAssemblerApplyOutcomeKind.HandoffFailed:
				throw new EngineResourceHandoffException(ApplyOperation, outcome.Compensation.GetValueOrDefault(),
					cause);
			default:
				ExceptionDispatchInfo.Throw(cause ?? new EngineBindingException(ApplyOperation));
				patch = null;
				return false;
		}
	}

	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "The apply, post-effect validation, rooting, snapshot and publication form one transaction.")]
	internal static AutoAssemblerApplyOutcome ApplyCore(string script, AutoAssemblerOptions options,
		AutoAssemblerDisableInfoTracker disableInfoTracker, AutoAssemblerPatchFactory patchFactory,
		bool createLegacyCause, out AutoAssemblerPatch? patch, out Exception? cause)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(script);
		ArgumentNullException.ThrowIfNull(options);
		options.Validate(nameof(options));
		ArgumentNullException.ThrowIfNull(disableInfoTracker);
		ArgumentNullException.ThrowIfNull(patchFactory);
		patch = null;
		cause = null;

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		// Stable for the whole admitted operation: a transition closes admission and drains it first.
		LuaStateIdentity runtime = LuaRuntime.CurrentStateIdentity;

		TargetSelectionObservation observation = TargetSelection.ObserveCurrent(state);
		if (!observation.IsQualified)
		{
			cause = new EngineTargetIdentityException(ApplyOperation,
				TargetSelection.CreateUnavailableCheck(observation));
			return Outcome(AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable, observation);
		}

		TargetProcessIncarnation incarnation = observation.Incarnation.GetValueOrDefault();
		LuaGlobalPushOutcome global = LuaGlobalFunctions.TryPushWithOutcome(state, SAutoAssemble, "autoAssemble"u8);
		if (!global.IsSuccess)
		{
			if (global.Status == LuaGlobalPushStatus.Unavailable)
			{
				cause = new EngineGlobalUnavailableException(ApplyOperation);
				return Outcome(AutoAssemblerApplyOutcomeKind.GlobalUnavailable, observation);
			}

			LuaStatus resolutionFailure = ToFailureStatus(global.LuaStatus);
			cause = new EngineLuaException(ApplyOperation, resolutionFailure);
			return Outcome(AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure, observation, resolutionFailure);
		}

		// autoAssemble(script) with three results: success, disable information or error detail, warnings. The
		// targetself argument is never passed.
		StringMarshaller.Push(state, script);
		LuaStatus status = state.TryCall(1, 3);
		if (!status.IsOk)
		{
			cause = createLegacyCause ? CreateLuaException(state, status, ApplyOperation) : null;
			return Outcome(AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure, observation, status);
		}

		int success = state.AbsoluteIndex(-3);
		int detail = success + 1;
		int warnings = success + 2;
		bool hasWarnings = !state.IsNil(warnings);
		AutoAssemblerHostTextCopy warningText =
			AutoAssemblerHostText.Copy(state, warnings, options.CaptureHostText, options.MaxHostTextBytes);
		if (state.TypeOf(success) != LuaType.Boolean)
		{
			cause = CreateUnexpectedResult(ApplyOperation, "a boolean success result", state.TypeOf(success));
			return new AutoAssemblerApplyOutcome(AutoAssemblerApplyOutcomeKind.InvalidResult, LuaStatus.Ok,
				observation, default, warningText, hasWarnings, null, null, null, false);
		}

		if (!state.ToBoolean(success))
		{
			AutoAssemblerHostTextCopy detailText =
				AutoAssemblerHostText.Copy(state, detail, options.CaptureHostText, options.MaxHostTextBytes);
			return new AutoAssemblerApplyOutcome(AutoAssemblerApplyOutcomeKind.Rejected, LuaStatus.Ok, observation,
				detailText, warningText, hasWarnings, null, null, null, false);
		}

		if (!state.IsTable(detail))
		{
			cause = CreateUnexpectedResult(ApplyOperation, "a disable-info table on success", state.TypeOf(detail));
			return new AutoAssemblerApplyOutcome(AutoAssemblerApplyOutcomeKind.InvalidResult, LuaStatus.Ok,
				observation, default, warningText, hasWarnings, null, null, null, false);
		}

		// Validate the captured target again inside the same operation: a change observed after the effect makes it
		// uncertain but keeps the only disable token, bound to the original incarnation.
		TargetIdentityCheck postCheck = TargetSelection.ValidateCurrent(state, incarnation);
		AutoAssemblerApplyOutcomeKind appliedKind = postCheck.IsCurrent
			? AutoAssemblerApplyOutcomeKind.Applied
			: AutoAssemblerApplyOutcomeKind.AppliedTargetChanged;
		EngineResourceOrigin origin = new(runtime, incarnation);

		// Retain the original table on the stack while the copy is rooted. A failed root leaves the original table as
		// the one remaining authority for a direct, target-checked disable.
		LuaRef? disableInfo = TryTrackDisableInfo(state, detail, warnings, disableInfoTracker, out Exception? trackingFailure);
		if (disableInfo is null)
		{
			cause = trackingFailure;
			TargetReleaseOutcome stackCompensation = TryDisableFromStack(script, state, detail, incarnation);
			return new AutoAssemblerApplyOutcome(AutoAssemblerApplyOutcomeKind.HandoffFailed, LuaStatus.Ok,
				observation, default, warningText, hasWarnings, postCheck, null, stackCompensation, false);
		}

		AutoAssemblerDisableInfoSnapshot? snapshot = null;
		try
		{
			snapshot = AutoAssemblerDisableInfoSnapshot.Read(state, detail, options);
			patch = patchFactory(script, disableInfo, origin, snapshot, postCheck) ??
					throw new InvalidOperationException("The Auto Assembler patch factory returned no patch.");
		}
		catch (Exception exception)
		{
			patch = null;
			cause = exception;
			TargetReleaseOutcome compensation = CompensateFailedPublication(script, disableInfo, origin);
			return new AutoAssemblerApplyOutcome(AutoAssemblerApplyOutcomeKind.HandoffFailed, LuaStatus.Ok,
				observation, default, warningText, hasWarnings, postCheck, snapshot, compensation, false);
		}

		return new AutoAssemblerApplyOutcome(appliedKind, LuaStatus.Ok, observation, default, warningText,
			hasWarnings, postCheck, snapshot, null, true);
	}

	// The owner always routes cleanup through this method. Keeping the LuaRef release in its finally block prevents a
	// failed protected call from pinning CE's disable-info table and makes retrying a possibly partial disable impossible.
	internal static TargetReleaseOutcome TryDisable(string script, LuaRef disableInfo, EngineResourceOrigin origin,
		out bool disableInvocationStarted)
	{
		ArgumentNullException.ThrowIfNull(disableInfo);
		disableInvocationStarted = false;
		if (TryRefuseBeforeInvocation(disableInfo, origin, out TargetReleaseOutcome refusal))
		{
			return refusal;
		}

		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			LuaState state = operation.State;
			try
			{
				using LuaFrame frame = new(state);
				TargetIdentityCheck targetCheck =
					TargetSelection.ValidateCurrent(state, origin.Target.GetValueOrDefault());
				if (!targetCheck.IsCurrent)
				{
					return TargetReleaseOutcome.Refused(targetCheck);
				}

				PushAutoAssemble(state, DisableOperation);
				StringMarshaller.Push(state, script);
				if (!state.TryPushRef(disableInfo))
				{
					return TargetReleaseOutcome.NotInvoked();
				}

				disableInvocationStarted = true;
				LuaStatus status = state.TryCall(2, 1);
				if (!status.IsOk)
				{
					throw CreateLuaException(state, status, DisableOperation);
				}

				if (state.TypeOf(-1) != LuaType.Boolean)
				{
					throw CreateUnexpectedResult(DisableOperation, "a boolean disable result", state.TypeOf(-1));
				}

				return state.ToBoolean(-1)
					? TargetReleaseOutcome.Released()
					: TargetReleaseOutcome.Unconfirmed(EngineFailureKind.ExpectedOperationFailure);
			}
			finally
			{
				// A disable can be partially applied even when CE returns false or raises. Ownership is therefore
				// consumed before the invocation and the registry table is unrooted on every result path.
				disableInfo.Release(state);
			}
		}
		catch
		{
			// Dispose still marks the reference released, without attempting a second operation against a detached
			// state. The caller uses disableInvocationStarted to preserve whether the CE call began.
			disableInfo.Dispose();
			throw;
		}
	}

	// A patch applied in another Lua universe (re-enable or controlled state replacement) is refused: its table belongs
	// to that universe. A detached runtime or a stale reference cannot begin a call either. Both consume the reference.
	private static bool TryRefuseBeforeInvocation(LuaRef disableInfo, EngineResourceOrigin origin,
		out TargetReleaseOutcome refusal)
	{
		if (!EngineResourceOrigin.IsCurrent(origin.Runtime))
		{
			disableInfo.Dispose();
			refusal = TargetReleaseOutcome.RefusedRuntimeChanged();
			return true;
		}

		if (!LuaRuntime.IsAttached || !disableInfo.IsCurrent)
		{
			disableInfo.Dispose();
			refusal = TargetReleaseOutcome.NotInvoked();
			return true;
		}

		refusal = default;
		return false;
	}

	private static LuaRef? TryTrackDisableInfo(LuaState state, int disableInfoIndex, int top,
		AutoAssemblerDisableInfoTracker disableInfoTracker, out Exception? failure)
	{
		failure = null;
		state.PushValue(disableInfoIndex);
		try
		{
			LuaRef disableInfo = disableInfoTracker(state);
			if (disableInfo is null)
			{
				throw new InvalidOperationException("The disable-info tracker returned no reference.");
			}

			return disableInfo;
		}
		catch (Exception exception)
		{
			state.SetTop(top);
			failure = exception;
			return null;
		}
	}

	private static LuaRef CreateDisableInfo(LuaState state)
	{
		return state.CreateRef();
	}

	private static AutoAssemblerPatch CreatePatch(string script, LuaRef disableInfo, EngineResourceOrigin origin,
		AutoAssemblerDisableInfoSnapshot snapshot, TargetIdentityCheck postApplyTargetCheck)
	{
		return new AutoAssemblerPatch(script, disableInfo, origin, snapshot, postApplyTargetCheck);
	}

	private static TargetReleaseOutcome CompensateFailedPublication(string script, LuaRef disableInfo,
		EngineResourceOrigin origin)
	{
		bool disableInvocationStarted = false;
		try
		{
			return TryDisable(script, disableInfo, origin, out disableInvocationStarted);
		}
		catch (EngineException exception)
		{
			return disableInvocationStarted
				? TargetReleaseOutcome.Unconfirmed(exception.Kind)
				: TargetReleaseOutcome.NotInvoked(exception.Kind);
		}
		catch (Exception)
		{
			return disableInvocationStarted
				? TargetReleaseOutcome.Unconfirmed(null)
				: TargetReleaseOutcome.NotInvoked();
		}
	}

	// The original disable-info table remains at disableInfoIndex and this helper deliberately does not root it. The
	// surrounding LuaFrame restores the stack after the one compensation attempt, including a failed protected call.
	private static TargetReleaseOutcome TryDisableFromStack(string script, LuaState state, int disableInfoIndex,
		TargetProcessIncarnation targetIncarnation)
	{
		bool disableInvocationStarted = false;
		try
		{
			TargetIdentityCheck targetCheck = TargetSelection.ValidateCurrent(state, targetIncarnation);
			if (!targetCheck.IsCurrent)
			{
				return TargetReleaseOutcome.Refused(targetCheck);
			}

			PushAutoAssemble(state, DisableOperation);
			StringMarshaller.Push(state, script);
			state.PushValue(disableInfoIndex);
			disableInvocationStarted = true;
			LuaStatus status = state.TryCall(2, 1);
			if (!status.IsOk)
			{
				return TargetReleaseOutcome.Unconfirmed(EngineFailureKind.ProtectedLuaFailure);
			}

			if (state.TypeOf(-1) != LuaType.Boolean)
			{
				return TargetReleaseOutcome.Unconfirmed(EngineFailureKind.MarshallingFailure);
			}

			return state.ToBoolean(-1)
				? TargetReleaseOutcome.Released()
				: TargetReleaseOutcome.Unconfirmed(EngineFailureKind.ExpectedOperationFailure);
		}
		catch (EngineException exception)
		{
			return disableInvocationStarted
				? TargetReleaseOutcome.Unconfirmed(exception.Kind)
				: TargetReleaseOutcome.NotInvoked(exception.Kind);
		}
		catch (Exception)
		{
			return disableInvocationStarted
				? TargetReleaseOutcome.Unconfirmed(null)
				: TargetReleaseOutcome.NotInvoked();
		}
	}

	private static void PushAutoAssemble(LuaState state, string operation)
	{
		LuaGlobalPushOutcome global = LuaGlobalFunctions.TryPushWithOutcome(state, SAutoAssemble, "autoAssemble"u8);
		if (global.IsSuccess)
		{
			return;
		}

		if (global.Status == LuaGlobalPushStatus.Unavailable)
		{
			throw new EngineGlobalUnavailableException(operation);
		}

		throw new EngineLuaException(operation, ToFailureStatus(global.LuaStatus));
	}

	private static AutoAssemblerApplyOutcome Outcome(AutoAssemblerApplyOutcomeKind kind,
		TargetSelectionObservation observation, LuaStatus luaStatus = default)
	{
		return new AutoAssemblerApplyOutcome(kind, luaStatus, observation, default, default, false, null, null, null,
			false);
	}

	// A resolution failure always carries a non-success status; never report a failure as LuaStatus.Ok.
	private static LuaStatus ToFailureStatus(LuaStatus status)
	{
		return status.IsOk ? LuaStatus.RuntimeError : status;
	}

	private static EngineLuaException CreateLuaException(LuaState state, LuaStatus status, string operation)
	{
		LuaError error = LuaError.FromStack(state, status);
		return new EngineLuaException(operation, status,
			"The protected Lua call for Engine operation '" + operation + "' failed.", new LuaException(error));
	}

	private static EngineMarshallingException CreateUnexpectedResult(string operation, string expected,
		LuaType actual)
	{
		return new EngineMarshallingException(operation, EngineMarshallingDirection.Result, expected,
			"a Lua " + actual.ToString().ToLowerInvariant() + " value");
	}
}
