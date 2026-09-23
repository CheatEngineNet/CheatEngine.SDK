namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Classifies the factual result of creating a plugin-owned memory-scan session.</summary>
/// <remarks>
///     The zero value is <see cref="Unknown" />; a default value never reads as success. The status distinguishes a
///     documented absent factory result from a protected Lua failure or an invalid host value. It is fixture/source
///     evidence for the creation binding, not a CE live-qualification claim.
/// </remarks>
public enum MemoryScanCreationStatus : byte
{
	/// <summary>No creation status has been observed; never produced by a completed factory call.</summary>
	Unknown = 0,

	/// <summary>The scanner and its distinct found-list child were created and adopted by a session.</summary>
	Success = 1,

	/// <summary>The required CE factory global was absent or was not callable.</summary>
	GlobalUnavailable = 2,

	/// <summary>A protected factory call failed before returning its documented result.</summary>
	LuaFailure = 3,

	/// <summary>The scanner factory returned its documented absent result, <see langword="nil" />.</summary>
	NoScannerResult = 4,

	/// <summary>The scanner factory returned a non-null value that was not a CE host object.</summary>
	InvalidScannerResult = 5,

	/// <summary>The found-list factory returned its documented absent result, <see langword="nil" />.</summary>
	NoFoundListResult = 6,

	/// <summary>The found-list factory returned a non-null value that was not a CE host object.</summary>
	InvalidFoundListResult = 7,

	/// <summary>The found-list factory returned the scanner object, so publishing a second owner was refused.</summary>
	AliasedFoundList = 8,

	/// <summary>A rollback destroy call began but Cheat Engine did not confirm all required cleanup operations.</summary>
	RollbackUnconfirmed = 9,

	/// <summary>
	///     The selected target could not be qualified before either target-dependent CE factory was invoked.
	/// </summary>
	TargetIdentityUnavailable = 10
}
