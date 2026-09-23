namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Classifies an explicit attempt to release a symbol registration or a symbol-list registration lease.</summary>
/// <remarks>
///     <see cref="Unknown" /> is the zero value: an outcome that was never assigned never reads as a release. Only
///     <see cref="Unknown" /> and <see cref="CleanupUnavailable" /> leave a lease retryable.
/// </remarks>
public enum SymbolRegistrationReleaseKind
{
	/// <summary>No release outcome was recorded: the value of <see langword="default" />.</summary>
	Unknown = 0,

	/// <summary>The lease's registration was removed from Cheat Engine's symbol handler.</summary>
	Released = 1,

	/// <summary>A terminal cleanup outcome had already been returned for this lease.</summary>
	AlreadyReleased = 2,

	/// <summary>A newer registration through this SDK coordinator replaced the lease, so no CE unregister was sent.</summary>
	Superseded = 3,

	/// <summary>
	///     The runtime is detached, or the Lua attach epoch or state generation changed, so no CE call was sent to another
	///     runtime.
	/// </summary>
	StaleRuntime = 4,

	/// <summary>
	///     No unregister call began because the runtime could not currently admit the operation, a required global or
	///     member was unavailable, or the lease's ownership of the name could not be verified. The lease stays retryable.
	/// </summary>
	CleanupUnavailable = 5,

	/// <summary>An unregister call began and failed, so the host-side registration is indeterminate and will not retry.</summary>
	CleanupIndeterminate = 6,

	/// <summary>
	///     The name now resolves to another address: a third party replaced the registration, so the lease skipped the
	///     unregister rather than remove the newer definition.
	/// </summary>
	Replaced = 7,

	/// <summary>The name no longer resolves: it was removed by someone else, so the lease skipped the unregister.</summary>
	ExternallyRemoved = 8
}
