using CheatEngine.SDK.Engine.Objects;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>Internal ownership-publication seam used only to prove the AOB result-list rollback.</summary>
/// <remarks>
///     This delegate is not a consumer extension point. <see cref="AobScanner" /> always publishes the caller-owned
///     <c>StringList</c> through its own SDK-sourced owner construction after a protected <c>AOBScan</c> call returned
///     a host object. Tests substitute a failing publisher to prove that the still-unpublished raw list is destroyed
///     exactly once and that the original failure propagates.
/// </remarks>
/// <param name="list">The borrowed handle of the list that <c>AOBScan</c> returned.</param>
/// <returns>The sole owner of that list.</returns>
internal delegate Owned<StringList> AobResultListPublisher(StringList list);
