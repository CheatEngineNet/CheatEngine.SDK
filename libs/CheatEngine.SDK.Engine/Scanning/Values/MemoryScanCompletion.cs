namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>The non-exceptional outcome of <see cref="MemoryScanSession.WaitForCompletion" />.</summary>
/// <remarks>
///     Cheat Engine 7.7 documents <c>MemScan.waitTillDone(timeout OPTIONAL)</c> as a boolean operation whose
///     <see langword="false" /> result means that the timeout elapsed. The initial SDK slice deliberately calls the
///     no-timeout form, but retains the result distinction rather than treating a CE timeout as a Lua failure.
/// </remarks>
public enum MemoryScanCompletion
{
    /// <summary>The scan completed and its found list was initialized for reading.</summary>
    Completed = 0,

    /// <summary>The host reported that its wait timed out; the session remains <see cref="MemoryScanState.Scanning" />.</summary>
    TimedOut = 1
}
