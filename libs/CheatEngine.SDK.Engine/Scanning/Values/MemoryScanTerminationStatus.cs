namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     Classifies the one cooperative stop request for a memory scan that may still be running: <c>terminateScan(false)</c>
///     followed by a bounded <c>waitTillDone(timeout)</c>.
/// </summary>
/// <remarks>
///     The zero value is <see cref="Unknown" />; a default value never reads as a confirmed stop. The SDK never forces
///     termination and never retries it: every value other than <see cref="Confirmed" /> and <see cref="NotRequired" />
///     means the scan may still be running.
/// </remarks>
public enum MemoryScanTerminationStatus : byte
{
	/// <summary>No termination status has been observed.</summary>
	Unknown = 0,

	/// <summary>No scan could be running, so no stop was requested.</summary>
	NotRequired = 1,

	/// <summary>CE accepted the cooperative stop and its bounded wait reported completion.</summary>
	Confirmed = 2,

	/// <summary>CE accepted the cooperative stop, but its bounded wait expired first; the stop is unconfirmed.</summary>
	WaitTimedOut = 3,

	/// <summary>The protected <c>terminateScan</c> call raised; no wait followed and the stop is unconfirmed.</summary>
	TerminateFailed = 4,

	/// <summary>
	///     The bounded wait after the stop request raised or returned something other than one boolean; the stop is
	///     unconfirmed.
	/// </summary>
	WaitFailed = 5,

	/// <summary>
	///     A scan may be running, but no CE call was allowed (worker thread, detached runtime, stale runtime or target
	///     context); the stop was not requested.
	/// </summary>
	NotInvoked = 6
}
