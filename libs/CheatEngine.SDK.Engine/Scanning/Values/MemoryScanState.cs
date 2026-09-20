namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     The local, conservative state of a <see cref="MemoryScanSession" />. This records what the session itself has
///     successfully requested and observed; it is not a replacement for Cheat Engine's internal scan state.
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine 7.7's <c>celua.txt</c> documents that a <c>MemScan</c> performs a first or next scan,
///         <c>waitTillDone</c> blocks until it finishes, and a <c>FoundList</c> must be initialized after the scan
///         before results are read. The transition model makes those prerequisites explicit:
///         <c>New -&gt; Scanning -&gt; ResultsReady</c>, and <c>ResultsReady -&gt; New</c> through <c>newScan</c>.
///     </para>
///     <para>
///         A failed protected call, including a <c>waitTillDone</c> call that could no longer establish whether CE is
///         still scanning or has completed, leaves the state <see cref="Invalidated" /> rather than guessing whether
///         Cheat Engine accepted a partial operation. Call <see cref="MemoryScanSession.Reset" /> to request a clean
///         <c>newScan</c> before starting another first scan.
///     </para>
/// </remarks>
public enum MemoryScanState
{
    /// <summary>The session has a scanner and result-list pair, but no readable scan result.</summary>
    New = 0,

    /// <summary>A first or next scan was accepted and has not yet completed successfully.</summary>
    Scanning = 1,

    /// <summary>
    ///     <c>waitTillDone</c> completed successfully and the attached found list initialized successfully.
    /// </summary>
    ResultsReady = 2,

    /// <summary>
    ///     A protected operation or result marshalling step failed after the session began a transition. The session
    ///     refuses reads and new scans until <see cref="MemoryScanSession.Reset" /> succeeds.
    /// </summary>
    Invalidated = 3,

    /// <summary>The session released its child list and then its scanner; no operation remains valid.</summary>
    Disposed = 4,
}
