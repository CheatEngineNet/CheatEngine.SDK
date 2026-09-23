using System;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>An explicit cleanup capability for a registration coordinated by this SDK instance.</summary>
/// <remarks>
///     <para>
///         Cheat Engine's <c>registerSymbol</c> API returns no opaque registration token, while
///         <c>unregisterSymbol(name)</c> removes solely by name. This lease therefore records the leased
///         <see cref="Address" /> and, before unregistering, resolves the name again: when it no longer resolves the
///         release reports <see cref="SymbolRegistrationReleaseKind.ExternallyRemoved" />, and when it resolves to another
///         address it reports <see cref="SymbolRegistrationReleaseKind.Replaced" />; neither sends an unregister, so a
///         newer third-party definition is never removed. The check is best effort and not atomic: a third party can
///         still replace the name between the lookup and the unregister, and a name that parses as an expression or that
///         collides case-insensitively with another symbol can produce a conservative <c>Replaced</c>. It must never be
///         interpreted as exclusive host-wide ownership. Leases made through
///         <see cref="SymbolRegistry.TryRegisterOwned" />
///         in this SDK instance additionally never unregister a newer lease's registration of the same name.
///     </para>
///     <para>
///         Cleanup is explicit and deterministic. There is no finalizer because calling CE from a finalizer thread is
///         unsafe. After an attach epoch or state-generation change, this lease becomes terminal without issuing an
///         unregister in the new runtime.
///     </para>
/// </remarks>
public sealed class SymbolRegistrationLease : IDisposable
{
	private SymbolRegistrationReleaseKind? _terminalKind;
	private bool _terminalOutcomeObserved;

	internal SymbolRegistrationLease(SymbolName name, Address address, SymbolRegistrationOptions options,
		LuaStateIdentity identity)
	{
		Name = name;
		Address = address;
		Options = options;
		Origin = new EngineResourceOrigin(identity, null);
	}

	/// <summary>Gets the registered name.</summary>
	public SymbolName Name
	{
		get;
	}

	/// <summary>Gets the target address the name was registered at: the value the release verifies before unregistering.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>
	///     Gets the Lua runtime identity in which the name was registered. A symbol registration is not bound to a target
	///     process incarnation.
	/// </summary>
	public EngineResourceOrigin Origin
	{
		get;
	}

	/// <summary>Gets the persistence option used when registering the name.</summary>
	public SymbolRegistrationOptions Options
	{
		get;
	}

	/// <summary>Gets whether this lease has reached a terminal outcome.</summary>
	public bool IsTerminal => _terminalKind.HasValue;

	internal LuaStateIdentity Identity => Origin.Runtime;

	/// <summary>Calls <see cref="Release" /> and intentionally discards its structured outcome.</summary>
	/// <remarks>
	///     A <see cref="SymbolRegistrationReleaseKind.CleanupUnavailable" /> outcome retains this lease so a caller
	///     may invoke <see cref="Release" /> later. A protected failure after CE cleanup starts is terminal and is not
	///     automatically retried.
	/// </remarks>
	public void Dispose()
	{
		_ = Release();
	}

	/// <summary>Attempts the coordinator-qualified unregister for this lease.</summary>
	/// <returns>A result that distinguishes no call, a protected failure after the call began, and a local supersession.</returns>
	public SymbolRegistrationReleaseOutcome Release()
	{
		return SymbolRegistry.ReleaseOwned(this);
	}

	internal void MarkTerminal(SymbolRegistrationReleaseKind kind)
	{
		_terminalKind ??= kind;
	}

	internal void MarkTerminalAndObserve(SymbolRegistrationReleaseKind kind)
	{
		MarkTerminal(kind);
		_terminalOutcomeObserved = true;
	}

	internal SymbolRegistrationReleaseKind? ObserveTerminalKind()
	{
		if (!_terminalKind.HasValue)
		{
			return null;
		}

		if (_terminalOutcomeObserved)
		{
			return SymbolRegistrationReleaseKind.AlreadyReleased;
		}

		_terminalOutcomeObserved = true;
		return _terminalKind.Value;
	}
}
