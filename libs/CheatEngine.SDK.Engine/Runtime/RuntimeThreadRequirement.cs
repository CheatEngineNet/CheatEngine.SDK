namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The thread-affinity fact attached to a capability contract.</summary>
public enum RuntimeThreadRequirement : byte
{
	/// <summary>The contract does not establish a thread requirement.</summary>
	Unknown = 0,

	/// <summary>The capability may be used on any thread subject to other runtime serialization rules.</summary>
	AnyThread = 1,

	/// <summary>The capability must execute on Cheat Engine's main GUI thread.</summary>
	MainThread = 2
}
