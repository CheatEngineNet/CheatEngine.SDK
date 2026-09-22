namespace CheatEngine.SDK.Abi.Native;

/// <summary>Result of a callback-registration teardown attempt.</summary>
internal enum ClassicDebugEventReleaseStatus
{
	Released,
	CallbackIsExecuting,
	ReleaseInProgress,
	UnregisterUnconfirmed
}
