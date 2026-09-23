using System;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>The factual result of a first-found AOB scan: the reported address, if any, and the session's release.</summary>
/// <remarks>
///     A first-found result is never a uniqueness proof, never the lowest address, and must never back a bounded or
///     range scan, a "require single" query, or any exhaustive query; use the exhaustive bounded route for those.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AobFirstFoundResult
{
	internal AobFirstFoundResult(AobFirstFoundOutcomeKind kind, Address address, MemoryScanCreationOutcome creation,
		LuaStatus luaStatus, TimeSpan hostScanElapsed, MemoryScanReleaseOutcome release)
	{
		Kind = kind;
		Address = address;
		Creation = creation;
		LuaStatus = luaStatus;
		HostScanElapsed = hostScanElapsed;
		Release = release;
	}

	/// <summary>Gets the factual outcome category.</summary>
	public AobFirstFoundOutcomeKind Kind
	{
		get;
	}

	/// <summary>
	///     Gets the address CE reported, for <see cref="AobFirstFoundOutcomeKind.Found" /> and
	///     <see cref="AobFirstFoundOutcomeKind.FoundOutsideBounds" />; otherwise zero.
	/// </summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets whether CE reported an address (<see cref="Address" /> is meaningful).</summary>
	public bool HasAddress => Kind is AobFirstFoundOutcomeKind.Found or AobFirstFoundOutcomeKind.FoundOutsideBounds;

	/// <summary>Gets the session factory outcome; the default value when no session creation was attempted.</summary>
	public MemoryScanCreationOutcome Creation
	{
		get;
	}

	/// <summary>
	///     Gets the protected Lua status of the failed call for <see cref="AobFirstFoundOutcomeKind.ScanFailed" />;
	///     otherwise <see cref="CheatEngine.SDK.Lua.Calls.LuaStatus.Ok" />.
	/// </summary>
	public LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>Gets the time from just before <c>firstScan</c> to the end of the wait.</summary>
	public TimeSpan HostScanElapsed
	{
		get;
	}

	/// <summary>Gets the one child-before-parent release of the session; the default value when none was created.</summary>
	public MemoryScanReleaseOutcome Release
	{
		get;
	}
}
