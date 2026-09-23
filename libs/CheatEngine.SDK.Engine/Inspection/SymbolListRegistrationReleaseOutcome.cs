using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>The structured outcome of releasing a <see cref="SymbolListRegistrationLease" />: unregister, then destroy.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct SymbolListRegistrationReleaseOutcome
{
	internal SymbolListRegistrationReleaseOutcome(SymbolRegistrationReleaseKind unregisterKind,
		LuaOperationStatus? unregisterStatus, TargetReleaseOutcome listRelease)
	{
		UnregisterKind = unregisterKind;
		UnregisterStatus = unregisterStatus;
		ListRelease = listRelease;
	}

	/// <summary>
	///     Gets how the unregister step ended: <see cref="SymbolRegistrationReleaseKind.Released" />,
	///     <see cref="SymbolRegistrationReleaseKind.AlreadyReleased" />, <see cref="SymbolRegistrationReleaseKind.StaleRuntime" />,
	///     <see cref="SymbolRegistrationReleaseKind.CleanupUnavailable" /> (retryable) or
	///     <see cref="SymbolRegistrationReleaseKind.CleanupIndeterminate" />.
	/// </summary>
	public SymbolRegistrationReleaseKind UnregisterKind
	{
		get;
	}

	/// <summary>
	///     Gets the status of the <c>unregister()</c> call; <see langword="null" /> when it was never called (the lease
	///     was already terminal, the runtime is stale, or no operation could be admitted). This is a nullable value
	///     rather than <see langword="default" />(<see cref="LuaOperationStatus" />) so a no-call outcome can never read
	///     as <see cref="LuaOperationStatus.Success" /> (A08-26).
	/// </summary>
	public LuaOperationStatus? UnregisterStatus
	{
		get;
	}

	/// <summary>
	///     Gets how the owned list ended: <see cref="TargetReleaseStatus.Released" /> after a confirmed destroy,
	///     <see cref="TargetReleaseStatus.UnconfirmedAfterInvocation" /> after a destroy that raised,
	///     <see cref="TargetReleaseStatus.RefusedRuntimeChanged" /> or <see cref="TargetReleaseStatus.NotInvoked" /> when
	///     the list was abandoned without any call, and <see cref="TargetReleaseStatus.Unspecified" /> while the list is
	///     still owned (a retryable outcome, or a compensation that left the list with its caller).
	/// </summary>
	public TargetReleaseOutcome ListRelease
	{
		get;
	}

	/// <summary>Gets whether no later release attempt can be made through the lease.</summary>
	public bool IsTerminal =>
		UnregisterKind is not (SymbolRegistrationReleaseKind.Unknown or SymbolRegistrationReleaseKind.CleanupUnavailable);
}
