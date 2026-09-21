using CheatEngine.SDK.Lua.Calls;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>The structured outcome of a coordinated symbol-registration cleanup attempt.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct SymbolRegistrationReleaseOutcome
{
    internal SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind kind, LuaOperationStatus status)
    {
        Kind = kind;
        Status = status;
    }

    /// <summary>Gets how cleanup progressed.</summary>
    public SymbolRegistrationReleaseKind Kind { get; }

    /// <summary>Gets CE's protected unregister status when a CE lookup or unregister was attempted.</summary>
    public LuaOperationStatus Status { get; }

    /// <summary>Gets whether no later explicit release attempt can be made through this lease.</summary>
    public bool IsTerminal => Kind is not SymbolRegistrationReleaseKind.CleanupUnavailable;
}
