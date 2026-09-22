using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>An immutable availability observation and its explicit contract metadata.</summary>
/// <param name="Capability">The SDK-owned capability identifier.</param>
/// <param name="State">The observed availability state.</param>
/// <param name="Contract">The evidence-backed constraints and normal return semantics.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct RuntimeCapabilityAvailability(
	RuntimeCapabilityId Capability,
	RuntimeCapabilityAvailabilityState State,
	RuntimeCapabilityContract Contract)
{
	/// <summary>Gets a value indicating whether the capability was explicitly observed as available.</summary>
	public bool IsAvailable => State == RuntimeCapabilityAvailabilityState.Available;

	/// <summary>Gets a value indicating whether the capability was explicitly observed as either available or unavailable.</summary>
	public bool IsKnown => State != RuntimeCapabilityAvailabilityState.Unknown;
}
