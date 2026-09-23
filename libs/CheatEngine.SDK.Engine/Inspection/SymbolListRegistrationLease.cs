using System;
using System.Threading;

using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     The sole owner of a plugin-created <see cref="SymbolList" /> while it is registered with Cheat Engine's symbol
///     handler. Releasing it unregisters the list first, then destroys it.
/// </summary>
/// <remarks>
///     <para>
///         Created by <see cref="SymbolLists.TryRegister" />, which transfers the list's <see cref="Owned{T}" /> into the
///         lease. The order of <see cref="Release" /> is fixed: an already terminal lease reports
///         <see cref="SymbolRegistrationReleaseKind.AlreadyReleased" />; a detached runtime or another Lua universe
///         reports
///         <see cref="SymbolRegistrationReleaseKind.StaleRuntime" /> and abandons the list without any Cheat Engine call;
///         an unavailable <c>unregister</c> member reports <see cref="SymbolRegistrationReleaseKind.CleanupUnavailable" />
///         and keeps the lease retryable; an <c>unregister()</c> that raised reports
///         <see cref="SymbolRegistrationReleaseKind.CleanupIndeterminate" /> and abandons the list <em>without</em>
///         destroying it (destroying a possibly registered list could leave a dangling entry in the symbol handler); a
///         confirmed <c>unregister()</c> is followed by the list's one destroy through
///         <see cref="Owned{T}.ReleaseWithOutcome" />.
///     </para>
///     <para>
///         The list is unregistered by object, not by name, so no target check is needed. There is no finalizer. The lease
///         is synchronized: concurrent releases make one attempt.
///     </para>
/// </remarks>
public sealed class SymbolListRegistrationLease : IDisposable
{
	private readonly Lock _gate = new();
	private Owned<SymbolList>? _list;
	private SymbolListRegistrationReleaseOutcome? _terminalOutcome;

	internal SymbolListRegistrationLease(Owned<SymbolList> list, bool registrationConfirmed)
	{
		ArgumentNullException.ThrowIfNull(list);
		_list = list;
		Origin = list.Origin;
		RegistrationConfirmed = registrationConfirmed;
	}

	/// <summary>Gets the Lua runtime identity that created the list. A symbol list is not bound to a target process.</summary>
	public EngineResourceOrigin Origin
	{
		get;
	}

	/// <summary>
	///     Gets whether Cheat Engine confirmed the <c>register()</c> call. <see langword="false" /> when the call raised
	///     after it began: the list may or may not be registered, so the lease still unregisters before destroying.
	/// </summary>
	public bool RegistrationConfirmed
	{
		get;
	}

	/// <summary>Gets the registered list as a borrowed handle.</summary>
	/// <exception cref="ObjectDisposedException">The lease reached a terminal outcome.</exception>
	public SymbolList List
	{
		get
		{
			lock (_gate)
			{
				if (_list is null || _list.IsDisposed)
				{
					throw new ObjectDisposedException(nameof(SymbolListRegistrationLease),
						"The symbol-list registration lease no longer owns its list.");
				}

				return _list.Value;
			}
		}
	}

	/// <summary>Gets whether the lease reached a terminal outcome.</summary>
	public bool IsTerminal
	{
		get
		{
			lock (_gate)
			{
				return _terminalOutcome.HasValue;
			}
		}
	}

	/// <summary>Calls <see cref="Release" /> and intentionally discards its structured outcome.</summary>
	public void Dispose()
	{
		_ = Release();
	}

	/// <summary>Unregisters the list, then destroys it, and reports both steps. Never throws.</summary>
	/// <returns>The outcome of both steps; see the type remarks for the order and the terminal cases.</returns>
	public SymbolListRegistrationReleaseOutcome Release()
	{
		lock (_gate)
		{
			if (_terminalOutcome.HasValue || _list is null)
			{
				return new SymbolListRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.AlreadyReleased,
					null, default);
			}

			Owned<SymbolList> list = _list;
			if (!LuaRuntime.IsAttached || !EngineResourceOrigin.IsCurrent(Origin.Runtime))
			{
				return Terminal(SymbolRegistrationReleaseKind.StaleRuntime, null, list.ReleaseWithOutcome());
			}

			if (!LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation))
			{
				// Attached, but no operation could be admitted (a transition, or no state for this thread): no call.
				return new SymbolListRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable,
					null, default);
			}

			LuaOperationStatus status;
			bool invoked;
			using (operation)
			{
				LuaState state = operation.State;
				using LuaFrame frame = new(state);
				status = list.Value.TryInvokeRegistration(state, "unregister"u8, out invoked);
			}

			if (status.IsSuccess)
			{
				return Terminal(SymbolRegistrationReleaseKind.Released, status, list.ReleaseWithOutcome());
			}

			if (!invoked)
			{
				// No unregister began (member absent, or its resolution failed): nothing changed, retry later.
				return new SymbolListRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable,
					status,
					default);
			}

			_ = list.Abandon();
			return Terminal(SymbolRegistrationReleaseKind.CleanupIndeterminate, status,
				TargetReleaseOutcome.NotInvoked(EngineFailureKind.ProtectedLuaFailure));
		}
	}

	private SymbolListRegistrationReleaseOutcome Terminal(SymbolRegistrationReleaseKind kind,
		LuaOperationStatus? status, TargetReleaseOutcome listRelease)
	{
		SymbolListRegistrationReleaseOutcome outcome = new(kind, status, listRelease);
		_terminalOutcome = outcome;
		_list = null;
		return outcome;
	}
}
