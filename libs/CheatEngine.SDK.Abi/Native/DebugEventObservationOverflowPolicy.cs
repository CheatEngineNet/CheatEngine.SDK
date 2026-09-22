namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     The overflow policy of a bounded copied-observation buffer.
/// </summary>
public enum DebugEventObservationOverflowPolicy
{
	/// <summary>Drops the incoming observation when the buffer is full.</summary>
	DropNewest = 0,

	/// <summary>Drops the oldest retained observation before storing the incoming observation when the buffer is full.</summary>
	DropOldest = 1
}
