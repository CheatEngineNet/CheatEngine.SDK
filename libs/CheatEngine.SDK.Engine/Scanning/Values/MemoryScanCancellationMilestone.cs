namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Records how a cancellation request intersected a synchronous memory-scan operation.</summary>
/// <remarks>
///     The session's cancellable wait uses CE's no-timeout <c>waitTillDone()</c> form, which has no cancellation
///     argument. CE 7.7.0.10621 also documents an optional timeout for <c>waitTillDone</c> (<c>celua.txt</c> line 2649);
///     that form is projected separately, and experimentally, by <see cref="MemoryScanSession.TryWaitForCompletion" />.
///     Neither form accepts a token, so a milestone is diagnostic evidence only: it never claims that a cancellation
///     request interrupted native scan work.
/// </remarks>
public enum MemoryScanCancellationMilestone : byte
{
	/// <summary>No cancellation request was observed by the most recent cancellable session operation.</summary>
	None = 0,

	/// <summary>Cancellation was observed before the SDK began its native CE call.</summary>
	CancelledBeforeNativeCall = 1,

	/// <summary>Cancellation was observed only after the synchronous native CE call had returned.</summary>
	ObservedAfterNativeCall = 2
}
