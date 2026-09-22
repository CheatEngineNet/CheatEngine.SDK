namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Result of attempting to install the internal classic debug-event dispatcher in a host table that has already
///     been copied and qualified by its caller.
/// </summary>
internal enum ClassicDebugEventRegistrationStatus
{
	Registered,
	UnsupportedArchitecture,
	MissingHostFunction,
	AnotherRegistrationIsActive,
	HostRejectedRegistration,
	RegistrationFault
}
