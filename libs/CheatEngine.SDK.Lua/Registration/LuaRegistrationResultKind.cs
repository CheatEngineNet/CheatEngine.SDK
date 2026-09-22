namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Classifies the factual result of publishing a Lua registration set.</summary>
public enum LuaRegistrationResultKind
{
	/// <summary>No registration was attempted.</summary>
	Unspecified = 0,

	/// <summary>Every entry was published and the returned lease owns the complete set.</summary>
	Succeeded = 1,

	/// <summary>Preflight found an existing effective global while the policy rejected replacement.</summary>
	Collision = 2,

	/// <summary>A protected operation failed before any requested global was published.</summary>
	PreflightFailed = 3,

	/// <summary>A protected operation failed during publication; the rollback report describes compensation.</summary>
	PublicationFailed = 4
}
