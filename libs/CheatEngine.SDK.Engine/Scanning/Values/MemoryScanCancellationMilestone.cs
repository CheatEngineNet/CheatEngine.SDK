namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Records how a cancellation request intersected a synchronous memory-scan operation.</summary>
/// <remarks>
///     Cheat Engine's documented <c>waitTillDone()</c> form has no cancellation or timeout parameter. A milestone is
///     therefore diagnostic evidence only: it never claims that a cancellation request interrupted native scan work.
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
