using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Calls;

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
	public SymbolRegistrationReleaseKind Kind
	{
		get;
	}

	/// <summary>
	///     Gets the status of the last CE call the release made: the unregister call, or the name lookup for
	///     <see cref="SymbolRegistrationReleaseKind.Replaced" />, <see cref="SymbolRegistrationReleaseKind.ExternallyRemoved" />
	///     and a lookup failure. <see langword="default" /> when no CE call was made
	///     (<see cref="SymbolRegistrationReleaseKind.AlreadyReleased" />, <see cref="SymbolRegistrationReleaseKind.Superseded" />,
	///     <see cref="SymbolRegistrationReleaseKind.StaleRuntime" /> before any call).
	/// </summary>
	/// <remarks>
	///     A lookup that failed with a protected Lua error is reported as a Lua failure with
	///     <see cref="Lua.Calls.LuaStatus.RuntimeError" />: the address-resolution primitive keeps the category, not the
	///     exact protected status.
	/// </remarks>
	public LuaOperationStatus Status
	{
		get;
	}

	/// <summary>Gets whether no later explicit release attempt can be made through this lease.</summary>
	public bool IsTerminal =>
		Kind is not (SymbolRegistrationReleaseKind.Unknown or SymbolRegistrationReleaseKind.CleanupUnavailable);
}
