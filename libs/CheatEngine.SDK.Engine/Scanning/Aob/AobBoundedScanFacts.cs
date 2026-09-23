using System;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>The mutable facts collected by one bounded scan before they are frozen into its result.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct AobBoundedScanFacts
{
	internal AobBoundedScanOutcomeKind Kind;
	internal MemoryScanCreationOutcome Creation;
	internal LuaStatus LuaStatus;
	internal ulong HostResultCount;
	internal int Written;
	internal ulong RowsRead;
	internal ulong UnreadHostRows;
	internal ulong BelowStartSkipped;
	internal ulong AtOrAfterStopSkipped;
	internal bool IsMaterializationLimitReached;
	internal string? HostErrorText;
	internal bool IsHostErrorTextTruncated;
	internal bool IsHostErrorTextUnreadable;
	internal TimeSpan HostScanElapsed;
	internal TimeSpan CopyElapsed;
	internal TimeSpan TotalElapsed;
	internal MemoryScanTerminationStatus Termination;
	internal MemoryScanReleaseOutcome Release;
}
