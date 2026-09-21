using CheatEngine.SDK.Engine.Objects;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Internal ownership-transfer seam used only to prove factory rollback behavior.</summary>
/// <remarks>
///     This delegate is not a consumer extension point. <see cref="MemoryScanSessions.TryCreate" /> always uses
///     <see cref="MemoryScanSession.Adopt" /> after the concrete CE factory calls succeed.
/// </remarks>
/// <param name="scanner">The factory-owned scanner.</param>
/// <param name="foundList">The factory-owned child list.</param>
/// <returns>The session that consumed both ownership wrappers.</returns>
internal delegate MemoryScanSession MemoryScanSessionAdopter(Owned<MemScan> scanner, Owned<FoundList> foundList);
