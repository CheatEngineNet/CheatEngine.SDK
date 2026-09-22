namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The architecture constraint attached to a capability contract.</summary>
public enum RuntimeArchitectureRequirement : byte
{
	/// <summary>The contract does not establish an architecture requirement.</summary>
	Unknown = 0,

	/// <summary>The capability applies to every architecture in the current contract's scope.</summary>
	Any = 1,

	/// <summary>The capability requires an x86 process.</summary>
	X86 = 2,

	/// <summary>The capability requires an x64 process.</summary>
	X64 = 3,

	/// <summary>The capability requires a 32-bit ARM process.</summary>
	Arm32 = 4,

	/// <summary>The capability requires a 64-bit ARM process.</summary>
	Arm64 = 5
}
