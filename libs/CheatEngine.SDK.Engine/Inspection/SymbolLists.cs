using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     Creates plugin-owned <see cref="SymbolList" /> objects, exposes Cheat Engine's main symbol list as a borrowed
///     handle, and registers an owned list through a <see cref="SymbolListRegistrationLease" />.
/// </summary>
/// <remarks>
///     <para>
///         Three things stay distinct: a symbol registered on its own (<see cref="SymbolRegistry" />), a symbol list owned
///         by the plugin (<see cref="TryCreate" />, an <see cref="Owned{T}" /> destroyed once), and the main list borrowed
///         from Cheat Engine (<see cref="TryGetMain" />, never destroyable, registrable or unregistrable through the SDK).
///     </para>
///     <para>
///         Only the zero-argument <c>createSymbolList()</c> is projected: its overload that takes an initial list and a
///         name registers the list automatically, which would publish a registration before its owner exists. Globals are
///         resolved through the SDK's cached, protected global push; no category is derived from Lua error text; a
///         detached runtime throws <see cref="InvalidOperationException" />.
///     </para>
/// </remarks>
public static class SymbolLists
{
	private static readonly LuaRef SCreateSymbolList = new();
	private static readonly LuaRef SGetMainSymbolList = new();

	/// <summary>Creates one empty, unregistered, plugin-owned symbol list with <c>createSymbolList()</c>.</summary>
	/// <param name="list">The owner on success; <see langword="null" /> otherwise.</param>
	/// <returns>
	///     <see cref="LuaOperationStatusKind.Success" /> with an owner,
	///     <see cref="LuaOperationStatusKind.GlobalUnavailable" />,
	///     <see cref="LuaOperationStatusKind.LuaFailure" />, <see cref="LuaOperationStatusKind.NilResult" />, or
	///     <see cref="LuaOperationStatusKind.InvalidResult" /> when Cheat Engine returned something that is not a host
	///     object.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryCreate(out Owned<SymbolList>? list)
	{
		list = null;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaOperationStatus status = TryCallGlobal(state, SCreateSymbolList, "createSymbolList"u8, out CEObject handle);
		if (!status.IsSuccess)
		{
			return status;
		}

		list = Publish(state, handle);
		return LuaOperationStatus.Success;
	}

	/// <summary>Gets Cheat Engine's main symbol list with <c>getMainSymbolList()</c>, as a borrowed handle only.</summary>
	/// <param name="list">A borrowed, Cheat-Engine-owned list; default on failure.</param>
	/// <returns>The protected call outcome, with the same categories as <see cref="TryCreate" />.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     The main list belongs to Cheat Engine's symbol handler: never destroy, register or unregister it. No SDK API
	///     accepts a borrowed <see cref="SymbolList" /> for those operations.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryGetMain([CEOwned] out SymbolList list)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaOperationStatus status = TryCallGlobal(state, SGetMainSymbolList, "getMainSymbolList"u8,
			out CEObject handle);
		list = status.IsSuccess ? SymbolList.FromHandle(handle) : default;
		return status;
	}

	/// <summary>
	///     Registers an owned list with Cheat Engine's symbol handler (<c>register()</c>) and transfers its ownership into a
	///     lease that unregisters before it destroys.
	/// </summary>
	/// <param name="list">The owned list; on a call that began, its ownership moves into <paramref name="lease" />.</param>
	/// <param name="lease">
	///     The lease after <c>register()</c> began: with <see cref="SymbolListRegistrationLease.RegistrationConfirmed" />
	///     <see langword="true" /> on success, <see langword="false" /> when the call raised; <see langword="null" /> when no
	///     call began (the owner then stays with the caller).
	/// </param>
	/// <returns>
	///     The status of <c>register()</c>. <see cref="LuaOperationStatusKind.LuaFailure" /> with a lease means the
	///     registration state is unknown: the list never returns to a caller who could destroy a possibly registered list.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="list" /> is <see langword="null" />.</exception>
	/// <exception cref="ObjectDisposedException"><paramref name="list" /> no longer owns a list.</exception>
	/// <exception cref="InvalidOperationException">
	///     The plugin is not enabled, the calling thread has no Lua state, or <paramref name="list" /> was created in a
	///     previous Lua runtime identity. Unlike a stale <see cref="Owned{T}" />'s own <c>Dispose</c>/<c>TryDestroy</c>/
	///     <see cref="Owned{T}.ReleaseWithOutcome" />, this throw does not consume <paramref name="list" />: no register
	///     call was attempted, so the caller can still dispose the list itself through its normal destroy path.
	/// </exception>
	/// <exception cref="SymbolListRegistrationHandoffException">
	///     Cheat Engine registered the list but the lease could not be constructed; the exception reports the one
	///     compensating <c>unregister()</c>.
	/// </exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryRegister(Owned<SymbolList> list, out SymbolListRegistrationLease? lease)
	{
		return TryRegisterCore(list, out lease, CreateLease);
	}

	// The factory is an internal test seam: it makes a managed failure after a successful register() deterministic
	// without letting consumers choose another ownership policy.
	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "The register call, ownership transfer and compensation form one transaction.")]
	internal static LuaOperationStatus TryRegisterCore(Owned<SymbolList> list, out SymbolListRegistrationLease? lease,
		SymbolListRegistrationLeaseFactory leaseFactory)
	{
		ArgumentNullException.ThrowIfNull(list);
		ArgumentNullException.ThrowIfNull(leaseFactory);
		lease = null;
		SymbolList value = list.Value;

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		if (!EngineResourceOrigin.IsCurrent(list.Origin.Runtime))
		{
			throw new InvalidOperationException(
				"The symbol list was created in a previous Lua runtime identity and cannot be registered.");
		}

		LuaState state = operation.State;
		LuaOperationStatus status;
		bool invoked;
		using (LuaFrame frame = new(state))
		{
			status = value.TryInvokeRegistration(state, "register"u8, out invoked);
		}

		if (!invoked)
		{
			return status;
		}

		Owned<SymbolList> transferred = list.PrepareTransfer();
		if (!status.IsSuccess)
		{
			// register() began and raised: keep the list out of the caller's hands, unconfirmed.
			lease = new SymbolListRegistrationLease(transferred, false);
			list.CompleteTransfer(transferred);
			return status;
		}

		try
		{
			lease = leaseFactory(transferred, true) ??
			        throw new InvalidOperationException(
				        "The symbol-list registration lease factory returned no lease.");
		}
		catch (Exception exception)
		{
			lease = null;
			SymbolListRegistrationReleaseOutcome compensation = CompensateFailedPublication(state, list);
			throw new SymbolListRegistrationHandoffException(compensation, exception);
		}

		list.CompleteTransfer(transferred);
		return status;
	}

	private static SymbolListRegistrationLease CreateLease(Owned<SymbolList> list, bool registrationConfirmed)
	{
		return new SymbolListRegistrationLease(list, registrationConfirmed);
	}

	// One unregister() after a register() whose lease could not be published. On success the caller keeps its
	// unregistered owner; when the unregister raised, the list is abandoned without destroy (it may still be registered).
	private static SymbolListRegistrationReleaseOutcome CompensateFailedPublication(LuaState state,
		Owned<SymbolList> list)
	{
		LuaOperationStatus status;
		bool invoked;
		using (LuaFrame frame = new(state))
		{
			status = list.Value.TryInvokeRegistration(state, "unregister"u8, out invoked);
		}

		if (status.IsSuccess)
		{
			return new SymbolListRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.Released, status, default);
		}

		if (!invoked)
		{
			return new SymbolListRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable, status,
				default);
		}

		_ = list.Abandon();
		return new SymbolListRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupIndeterminate, status,
			TargetReleaseOutcome.NotInvoked(EngineFailureKind.ProtectedLuaFailure));
	}

	// Calls a zero-argument global that returns one host object.
	private static LuaOperationStatus TryCallGlobal(LuaState state, LuaRef cache, ReadOnlySpan<byte> name,
		out CEObject handle)
	{
		handle = CEObject.Null;
		LuaGlobalPushOutcome global = LuaGlobalFunctions.TryPushWithOutcome(state, cache, name);
		if (!global.IsSuccess)
		{
			return global.Status == LuaGlobalPushStatus.Unavailable
				? LuaOperationStatus.GlobalUnavailable
				: LuaOperationStatus.LuaFailure(global.LuaStatus.IsOk ? LuaStatus.RuntimeError : global.LuaStatus);
		}

		LuaStatus status = state.TryCall(0, 1);
		if (!status.IsOk)
		{
			return LuaOperationStatus.LuaFailure(status);
		}

		if (state.IsNil(-1))
		{
			return LuaOperationStatus.NilResult;
		}

		return CEObject.TryRead(state, -1, out handle) ? LuaOperationStatus.Success : LuaOperationStatus.InvalidResult;
	}

	// Between the read of a caller-owned handle and the publication of its owner, the raw handle is the only authority
	// able to destroy the object (audit A08-09). If publication throws, destroy it once, then report the failure.
	private static Owned<SymbolList> Publish(LuaState state, CEObject handle)
	{
		try
		{
			return new Owned<SymbolList>(SymbolList.FromHandle(handle));
		}
		catch (Exception)
		{
			RollBack(state, handle);
			throw;
		}
	}

	private static void RollBack(LuaState state, CEObject handle)
	{
		using LuaFrame rollback = new(state);
		try
		{
			_ = handle.TryDestroy(state);
		}
		catch (Exception)
		{
			// The publication failure is the primary cause; a push failure here must not replace it, and the one
			// destroy attempt is never retried.
		}
	}
}
