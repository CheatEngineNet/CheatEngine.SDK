namespace CheatEngine.SDK.Engine.Objects;

/// <summary>
///     How far an effectful Cheat Engine operation went, as far as the SDK can establish it without parsing host text.
/// </summary>
/// <remarks>
///     <para>
///         The audit's three effect states (not started, applied, unknown) are extended by <see cref="NotApplied" />: a
///         documented negative result is a fact, and merging it into <see cref="Unknown" /> would lose information
///         (ADR-08). Cheat Engine gives no factual signal for a partial effect, so a partial effect is reported as
///         <see cref="Unknown" />.
///     </para>
///     <para>
///         <see cref="Unknown" /> is the zero value: an outcome that was never assigned never reads as an established
///         effect.
///     </para>
/// </remarks>
public enum EngineEffectState
{
	/// <summary>
	///     An effect may or may not have happened: a Cheat Engine error, a protected failure after the call began, a
	///     rejection that does not prove the absence of changes, or an observation change after the effect.
	/// </summary>
	Unknown = 0,

	/// <summary>No effectful Cheat Engine call began.</summary>
	NotStarted = 1,

	/// <summary>
	///     Cheat Engine completed the call and returned its documented negative result (for example <c>nil</c> from
	///     <c>allocateMemory</c>).
	/// </summary>
	NotApplied = 2,

	/// <summary>Cheat Engine confirmed the effect.</summary>
	Applied = 3
}
