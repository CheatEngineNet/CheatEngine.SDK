using System;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>Records the facts used to identify the currently selected Cheat Engine target.</summary>
/// <remarks>
///     These flags describe observations, not a universal process lock. A caller must refuse a target-mutating
///     operation when the required facts are absent rather than treating a PID or target architecture as an identity.
///     <see cref="LocalProcessStartTime" /> is emitted only together with <see cref="LocalBackendConfirmed" />: a local
///     creation time says nothing about a PID served remotely by CEServer (audit A12-05) or about a file opened as a
///     process (A12-07).
/// </remarks>
[Flags]
public enum TargetIdentityEvidence : byte
{
	/// <summary>No target-identity fact is available.</summary>
	None = 0,

	/// <summary>Cheat Engine reported the numeric identifier of its selected process.</summary>
	CheatEngineSelectedProcessId = 1,

	/// <summary>The local operating system supplied the selected process's creation time.</summary>
	LocalProcessStartTime = 2,

	/// <summary>
	///     In the same Lua operation, Cheat Engine's <c>isConnectedToCEServer</c> returned <see langword="false" />, so the
	///     selected PID denotes a process of the local machine.
	/// </summary>
	LocalBackendConfirmed = 4
}
