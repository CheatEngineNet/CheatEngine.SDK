namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Handles one copied debug-event observation and returns its complete synchronous disposition.
/// </summary>
/// <param name="observation">The scalar copy captured while the native callback was active.</param>
/// <returns>The disposition the SDK must settle before its native callback returns.</returns>
public delegate DebugEventDecision DebugEventDecisionHandler(in DebugEventObservation observation);
