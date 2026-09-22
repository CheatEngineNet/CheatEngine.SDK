using System;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>Records the facts used to identify the currently selected Cheat Engine target.</summary>
/// <remarks>
///     These flags describe observations, not a universal process lock. A caller must refuse a target-mutating
///     operation when the required facts are absent rather than treating a PID or target architecture as an identity.
/// </remarks>
[Flags]
public enum TargetIdentityEvidence : byte
{
	/// <summary>No target-identity fact is available.</summary>
	None = 0,

	/// <summary>Cheat Engine reported the numeric identifier of its selected process.</summary>
	CheatEngineSelectedProcessId = 1,

	/// <summary>The local operating system supplied the selected process's creation time.</summary>
	LocalProcessStartTime = 2
}
