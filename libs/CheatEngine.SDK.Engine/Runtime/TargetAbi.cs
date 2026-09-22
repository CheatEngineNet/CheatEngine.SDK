namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The target-process ABI family documented by CE 7.7's <c>getABI</c> global.</summary>
public enum TargetAbi : byte
{
	/// <summary>No target ABI fact is available.</summary>
	Unknown = 0,

	/// <summary>The Windows calling-convention family.</summary>
	Windows = 1,

	/// <summary>The Unix/Linux calling-convention family.</summary>
	Unix = 2
}
