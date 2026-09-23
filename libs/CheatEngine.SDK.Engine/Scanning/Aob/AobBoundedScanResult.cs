using System;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>
///     The factual result of a bounded, exhaustive AOB scan: its outcome, the four scan limits it met, the copy
///     accounting, CE's error text, separate host-scan and copy durations, and the session's release.
/// </summary>
/// <remarks>
///     <para>
///         Four limits are distinct: the <b>CE work limit</b> is the <see cref="AobScanBounds" /> passed to CE;
///         <see cref="HostResultCount" /> is the number of <b>available results</b> CE reported (including rows outside
///         the bounds); the <b>materialization limit</b> is the caller's destination length, reached when
///         <see cref="IsMaterializationLimitReached" />; the <b>call deadline</b> is the optional wait timeout, whose
///         expiry is <see cref="AobBoundedScanOutcomeKind.WaitTimedOut" />.
///     </para>
///     <para>
///         The accounting fields describe what the SDK read even on a failure; <see cref="Written" /> is non-zero only for
///         a success, because nothing is published otherwise. Addresses are copied in CE's found-list order. The C3
///         spike observed ascending order, but CE does not document one: never infer "lowest address" from the first
///         element. Uniqueness needs an exhausted domain (<see cref="InBoundsCountIsExact" />) or a second in-bounds match
///         (<see cref="Written" /> of two or more), never a first-found scan.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AobBoundedScanResult
{
	internal AobBoundedScanResult(in AobBoundedScanFacts facts)
	{
		Kind = facts.Kind;
		Creation = facts.Creation;
		LuaStatus = facts.LuaStatus;
		HostResultCount = facts.HostResultCount;
		Written = facts.Written;
		RowsRead = facts.RowsRead;
		UnreadHostRows = facts.UnreadHostRows;
		BelowStartSkipped = facts.BelowStartSkipped;
		AtOrAfterStopSkipped = facts.AtOrAfterStopSkipped;
		IsMaterializationLimitReached = facts.IsMaterializationLimitReached;
		HostErrorText = facts.HostErrorText;
		IsHostErrorTextTruncated = facts.IsHostErrorTextTruncated;
		IsHostErrorTextUnreadable = facts.IsHostErrorTextUnreadable;
		HostScanElapsed = facts.HostScanElapsed;
		CopyElapsed = facts.CopyElapsed;
		TotalElapsed = facts.TotalElapsed;
		Termination = facts.Termination;
		Release = facts.Release;
	}

	/// <summary>Gets the factual outcome category.</summary>
	public AobBoundedScanOutcomeKind Kind
	{
		get;
	}

	/// <summary>
	///     Gets whether addresses were published: <see cref="AobBoundedScanOutcomeKind.Matches" /> or
	///     <see cref="AobBoundedScanOutcomeKind.NoMatches" />.
	/// </summary>
	public bool IsSuccess => Kind is AobBoundedScanOutcomeKind.Matches or AobBoundedScanOutcomeKind.NoMatches;

	/// <summary>
	///     Gets the session factory outcome, including the target observation made before the scanner was created; the
	///     default value when no session creation was attempted.
	/// </summary>
	public MemoryScanCreationOutcome Creation
	{
		get;
	}

	/// <summary>
	///     Gets the protected Lua status of the failed call for <see cref="AobBoundedScanOutcomeKind.ScanFailed" />;
	///     otherwise <see cref="CheatEngine.SDK.Lua.Calls.LuaStatus.Ok" />.
	/// </summary>
	public LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>Gets the available-results count CE reported for the found list, including rows outside the bounds.</summary>
	public ulong HostResultCount
	{
		get;
	}

	/// <summary>Gets how many in-bounds addresses were copied to the destination; zero unless the scan succeeded.</summary>
	public int Written
	{
		get;
	}

	/// <summary>Gets how many found-list rows were read (one <c>getAddress</c> call each).</summary>
	public ulong RowsRead
	{
		get;
	}

	/// <summary>Gets how many available rows were not read, because the destination was full or the copy stopped.</summary>
	public ulong UnreadHostRows
	{
		get;
	}

	/// <summary>
	///     Gets how many returned addresses lay below <see cref="AobScanBounds.Start" /> and were dropped: CE's start
	///     bound is not byte-exact on the pinned profile (spike D4.2).
	/// </summary>
	public ulong BelowStartSkipped
	{
		get;
	}

	/// <summary>
	///     Gets how many returned addresses lay at or above <see cref="AobScanBounds.Stop" /> and were dropped; expected to
	///     be zero because CE honours the stop bound (spike D4.1).
	/// </summary>
	public ulong AtOrAfterStopSkipped
	{
		get;
	}

	/// <summary>Gets whether the destination filled up while unread rows remained (the materialization limit).</summary>
	public bool IsMaterializationLimitReached
	{
		get;
	}

	/// <summary>
	///     Gets whether the in-bounds count of a successful scan is exact, i.e. every available row was read, so that
	///     <see cref="Written" /> is the number of in-bounds matches.
	/// </summary>
	public bool InBoundsCountIsExact => IsSuccess && UnreadHostRows == 0;

	/// <summary>
	///     Gets CE's non-empty <c>ErrorString</c> text, copied and bounded to
	///     <see cref="MemoryScanSession.HostErrorTextMaximumUtf8Bytes" /> UTF-8 bytes; <see langword="null" /> when it was
	///     empty, unreadable or never read. It is a fact, never parsed or classified.
	/// </summary>
	public string? HostErrorText
	{
		get;
	}

	/// <summary>Gets whether <see cref="HostErrorText" /> is a prefix of a longer host text.</summary>
	public bool IsHostErrorTextTruncated
	{
		get;
	}

	/// <summary>Gets whether reading CE's error text failed; this never changes <see cref="Kind" />.</summary>
	public bool IsHostErrorTextUnreadable
	{
		get;
	}

	/// <summary>Gets the time from just before <c>firstScan</c> to the end of the successful wait: CE's scan cost.</summary>
	public TimeSpan HostScanElapsed
	{
		get;
	}

	/// <summary>Gets the time spent reading the count, the error text and the rows: the copy cost.</summary>
	public TimeSpan CopyElapsed
	{
		get;
	}

	/// <summary>Gets the time from session creation to the end of its release.</summary>
	public TimeSpan TotalElapsed
	{
		get;
	}

	/// <summary>
	///     Gets the cooperative stop the route requested after its call deadline expired;
	///     <see cref="MemoryScanTerminationStatus.NotRequired" /> when it requested none.
	/// </summary>
	public MemoryScanTerminationStatus Termination
	{
		get;
	}

	/// <summary>Gets the one child-before-parent release of the session; the default value when none was created.</summary>
	public MemoryScanReleaseOutcome Release
	{
		get;
	}
}
