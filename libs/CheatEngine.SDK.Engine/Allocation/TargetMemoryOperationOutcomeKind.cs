namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     Categorizes the factual result of an allocation-related Cheat Engine operation without exposing a Lua error
///     object or parsing error text.
/// </summary>
/// <remarks>
///     The values intentionally describe only the allocation binding boundary. They are not a universal result model:
///     allocation addresses and any future domain-specific effect details remain on their own result types.
/// </remarks>
public enum TargetMemoryOperationOutcomeKind
{
	/// <summary>The value was not initialized by an operation.</summary>
	Unspecified = 0,

	/// <summary>The operation completed with its documented successful result.</summary>
	Succeeded = 1,

	/// <summary>
	///     Cheat Engine completed the call and returned its documented negative result, such as <see langword="nil" />
	///     from <c>allocateMemory</c> or <see langword="false" /> from <c>deAlloc</c>.
	/// </summary>
	ExpectedFailure = 2,

	/// <summary>The required Cheat Engine binding global was unavailable or non-callable.</summary>
	GlobalUnavailable = 3,

	/// <summary>A selected optional Engine capability was unavailable.</summary>
	CapabilityUnavailable = 4,

	/// <summary>The protected Lua call failed before it produced its declared result.</summary>
	ProtectedLuaFailure = 5,

	/// <summary>The binding could not uphold its declared contract.</summary>
	BindingFailure = 6,

	/// <summary>The call result or an input could not be represented by the declared contract.</summary>
	MarshallingFailure = 7,

	/// <summary>The target identity required to begin the operation could not be established.</summary>
	TargetIdentityUnavailable = 8,

	/// <summary>The current target no longer matches the target-bound owner.</summary>
	TargetIdentityMismatch = 9
}
