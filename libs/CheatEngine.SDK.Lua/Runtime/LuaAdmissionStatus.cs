namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     The factual reason <see cref="LuaRuntime.TryAcquireOperationWithOutcome" /> did, or did not, admit an
///     operation.
/// </summary>
/// <remarks>
///     The zero value is <see cref="Unknown" />; a default value never reads as admitted. Callers must switch on this
///     enum instead of parsing an exception message: <see cref="LuaRuntime.AcquireOperation()" /> throws a
///     reason-specific message built from the same categories, but the category itself is never derived from text.
/// </remarks>
public enum LuaAdmissionStatus
{
	/// <summary>No outcome has been observed.</summary>
	Unknown = 0,

	/// <summary>The operation was admitted; the host provided a Lua state for the calling thread.</summary>
	Admitted = 1,

	/// <summary>No host binding is attached: the plugin is not enabled.</summary>
	Detached = 2,

	/// <summary>A lifecycle transition (attach, detach or state reset) has closed admission and is draining.</summary>
	TransitionInProgress = 3,

	/// <summary>A binding is attached and admission is open, but the host returned no Lua state for this thread.</summary>
	NoStateForThread = 4,

	/// <summary>
	///     The calling thread is not the host's captured main thread, is not inside a host-invoked callback, and the
	///     conservative default (<see cref="LuaThreadAdmission.MainThreadOnly" />) refused it before the state provider
	///     ran. See <see cref="LuaRuntime.AdmitWorkerThreads" />.
	/// </summary>
	ThreadNotAdmitted = 5,

	/// <summary>
	///     An external reset of the host's Lua state was detected: this SDK copy's universe stamp no longer matches. Every
	///     admission path refuses until the next <see cref="LuaRuntime.Attach" />.
	/// </summary>
	ExternalStateReset = 6
}
