using System;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>An explicit cleanup capability for a registration coordinated by this SDK instance.</summary>
/// <remarks>
///     <para>
///         Cheat Engine's <c>registerSymbol</c> API returns no opaque registration token, while
///         <c>unregisterSymbol(name)</c> removes solely by name. Consequently this lease can prevent an older lease
///         from deleting a newer registration made through <see cref="SymbolRegistry.TryRegisterOwned" /> in this SDK
///         instance; it cannot prove that a Lua script, plugin, another SDK copy, or direct CE call has not replaced the
///         name. It must never be interpreted as exclusive host-wide ownership.
///     </para>
///     <para>
///         Cleanup is explicit and deterministic. There is no finalizer because calling CE from a finalizer thread is
///         unsafe. After an attach epoch or state-generation change, this lease becomes terminal without issuing an
///         unregister in the new runtime.
///     </para>
/// </remarks>
public sealed class SymbolRegistrationLease : IDisposable
{
    private readonly SymbolName _name;
    private readonly SymbolRegistrationOptions _options;
    private SymbolRegistrationReleaseKind? _terminalKind;
    private bool _terminalOutcomeObserved;

    internal SymbolRegistrationLease(SymbolName name, SymbolRegistrationOptions options, LuaStateIdentity identity)
    {
        _name = name;
        _options = options;
        Identity = identity;
    }

    /// <summary>Gets the registered name.</summary>
    public SymbolName Name => _name;

    /// <summary>Gets the persistence option used when registering the name.</summary>
    public SymbolRegistrationOptions Options => _options;

    /// <summary>Gets whether this lease has reached a terminal outcome.</summary>
    public bool IsTerminal => _terminalKind.HasValue;

    /// <summary>Attempts the coordinator-qualified unregister for this lease.</summary>
    /// <returns>A result that distinguishes no call, a protected failure after the call began, and a local supersession.</returns>
    public SymbolRegistrationReleaseOutcome Release()
    {
        return SymbolRegistry.ReleaseOwned(this);
    }

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

    internal LuaStateIdentity Identity { get; }

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
        if (!_terminalKind.HasValue) return null;
        if (_terminalOutcomeObserved) return SymbolRegistrationReleaseKind.AlreadyReleased;

        _terminalOutcomeObserved = true;
        return _terminalKind.Value;
    }
}
