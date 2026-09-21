using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>The result of registering a symbol through the SDK ownership coordinator.</summary>
public readonly struct SymbolRegistrationAcquireOutcome
{
    internal SymbolRegistrationAcquireOutcome(LuaOperationStatus status, SymbolRegistrationLease? lease)
    {
        Status = status;
        Lease = lease;
    }

    /// <summary>Gets the protected CE registration status.</summary>
    public LuaOperationStatus Status { get; }

    /// <summary>Gets the coordinated cleanup lease only when <see cref="Status" /> is successful.</summary>
    public SymbolRegistrationLease? Lease { get; }

    /// <summary>Gets whether a coordinated cleanup lease was created.</summary>
    public bool HasLease => Lease is not null;
}
