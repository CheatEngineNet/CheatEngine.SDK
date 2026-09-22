using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     Applies an Auto Assembler script against the current Cheat Engine target and creates the corresponding owned
///     disable lease.
/// </summary>
/// <remarks>
///     This is deliberately the low-level SDK escape hatch. It passes only the script to CE's
///     <c>autoAssemble</c> global and never supplies CE's <c>targetSelf</c> argument. A caller that reaches this API
///     is responsible for the script's content; higher-level APIs should expose typed, capability-gated patches
///     rather than arbitrary script execution. The SDK captures a qualified target incarnation before it applies a
///     script and validates it before disabling the resulting owner; it never supplies <c>targetSelf</c>, opens, or
///     selects a process. CE exposes no inspected primitive that binds that observation atomically to the following
///     ambient-target call, so an external selection transition in that interval is not live-qualified as safe.
/// </remarks>
public static class AutoAssemblerPatcher
{
	private const string ApplyOperation = "AutoAssemblerApply";
	private const string DisableOperation = "AutoAssemblerDisable";

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
	[RequiresPluginEnabled]
	public static bool TryApply(string script, [NotNullWhen(true)] out AutoAssemblerPatch? patch)
	{
		return TryApplyCore(script, out patch, CreateDisableInfo, CreatePatch);
	}

	// The seams are internal test infrastructure. A caller cannot select tracking or ownership behavior; they let the
	// SDK prove that every exception between a successful apply and publication retains one compensation authority.
	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "This internal seam must keep the apply, compensation, and publication transaction together.")]
	internal static bool TryApplyCore(string script, [NotNullWhen(true)] out AutoAssemblerPatch? patch,
		AutoAssemblerDisableInfoTracker disableInfoTracker, AutoAssemblerPatchFactory patchFactory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(script);
		ArgumentNullException.ThrowIfNull(disableInfoTracker);
		ArgumentNullException.ThrowIfNull(patchFactory);
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);

		TargetSelectionObservation targetObservation = TargetSelection.ObserveCurrent(state);
		if (!targetObservation.IsQualified)
		{
			throw new EngineTargetIdentityException(ApplyOperation,
				TargetSelection.CreateUnavailableCheck(targetObservation));
		}

		PushAutoAssemble(state, ApplyOperation);
		StringMarshaller.Push(state, script);
		LuaStatus status = state.TryCall(1, 2);
		if (!status.IsOk)
		{
			ThrowLua(state, status, ApplyOperation);
		}

		if (state.TypeOf(-2) != LuaType.Boolean)
		{
			ThrowUnexpectedResult(ApplyOperation, "a boolean success result", state.TypeOf(-2));
		}

		if (!state.ToBoolean(-2))
		{
			patch = null;
			return false;
		}

		if (!state.IsTable(-1))
		{
			ThrowUnexpectedResult(ApplyOperation, "a disable-info table on success", state.TypeOf(-1));
		}

		// Retain the original table on the stack while the copy is rooted. A protected ref failure consumes only the
		// copy and leaves the original table as the one remaining authority for a direct, target-checked disable.
		int disableInfoIndex = state.AbsoluteIndex(-1);
		state.PushValue(disableInfoIndex);
		LuaRef disableInfo;
		try
		{
			disableInfo = disableInfoTracker(state);
			if (disableInfo is null)
			{
				throw new InvalidOperationException("The disable-info tracker returned no reference.");
			}
		}
		catch (Exception exception)
		{
			state.SetTop(disableInfoIndex);
			TargetReleaseOutcome cleanupOutcome = TryDisableFromStack(script, state, disableInfoIndex,
				targetObservation.Incarnation.GetValueOrDefault());
			throw new EngineResourceHandoffException(ApplyOperation, cleanupOutcome, exception);
		}

		try
		{
			patch = patchFactory(script, disableInfo, targetObservation.Incarnation.GetValueOrDefault());
			return true;
		}
		catch (Exception exception)
		{
			TargetReleaseOutcome cleanupOutcome = CompensateFailedPublication(script, disableInfo,
				targetObservation.Incarnation.GetValueOrDefault());
			throw new EngineResourceHandoffException(ApplyOperation, cleanupOutcome, exception);
		}
	}

	// The owner always routes cleanup through this method. Keeping the LuaRef release in its finally block prevents a
	// failed protected call from pinning CE's disable-info table and makes retrying a possibly partial disable impossible.
	internal static TargetReleaseOutcome TryDisable(string script, LuaRef disableInfo, TargetProcessIncarnation target,
		out bool disableInvocationStarted)
	{
		ArgumentNullException.ThrowIfNull(disableInfo);
		disableInvocationStarted = false;

		if (!LuaRuntime.IsAttached || !disableInfo.IsCurrent)
		{
			disableInfo.Dispose();
			return TargetReleaseOutcome.NotInvoked();
		}

		try
		{
			using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			LuaState state = operation.State;
			try
			{
				using LuaFrame frame = new(state);
				TargetIdentityCheck targetCheck = TargetSelection.ValidateCurrent(state, target);
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
					ThrowLua(state, status, DisableOperation);
				}

				if (state.TypeOf(-1) != LuaType.Boolean)
				{
					ThrowUnexpectedResult(DisableOperation, "a boolean disable result", state.TypeOf(-1));
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

	private static LuaRef CreateDisableInfo(LuaState state)
	{
		return state.CreateRef();
	}

	private static AutoAssemblerPatch CreatePatch(string script, LuaRef disableInfo,
		TargetProcessIncarnation targetIncarnation)
	{
		return new AutoAssemblerPatch(script, disableInfo, targetIncarnation);
	}

	private static TargetReleaseOutcome CompensateFailedPublication(string script, LuaRef disableInfo,
		TargetProcessIncarnation targetIncarnation)
	{
		bool disableInvocationStarted = false;
		try
		{
			return TryDisable(script, disableInfo, targetIncarnation, out disableInvocationStarted);
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
		LuaStatus status = state.TryGetGlobal("autoAssemble"u8);
		if (!status.IsOk)
		{
			ThrowLua(state, status, operation);
		}

		if (!state.IsFunction(-1))
		{
			throw new EngineGlobalUnavailableException(operation);
		}
	}

	[DoesNotReturn]
	private static void ThrowLua(LuaState state, LuaStatus status, string operation)
	{
		LuaError error = LuaError.FromStack(state, status);
		throw new EngineLuaException(operation, status,
			"The protected Lua call for Engine operation '" + operation + "' failed.", new LuaException(error));
	}

	[DoesNotReturn]
	private static void ThrowUnexpectedResult(string operation, string expected, LuaType actual)
	{
		throw new EngineMarshallingException(operation, EngineMarshallingDirection.Result, expected,
			"a Lua " + actual.ToString().ToLowerInvariant() + " value");
	}
}
