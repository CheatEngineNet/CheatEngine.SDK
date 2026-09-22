namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     The synchronous decision returned by a classic debug-event handler.
/// </summary>
/// <remarks>
///     The current CE 7.7 x64 profile has no qualified <c>ContinueDebugEvent</c> projection. Consequently,
///     <see cref="PluginOwnsContinuation" /> is deliberately rejected to
///     <see cref="ContinueWithCheatEngine" /> by the SDK's classic dispatcher. It exists so a future, exact-host
///     profile can add an SDK-owned continuation invocation without changing the decision delegate into an asynchronous
///     contract.
/// </remarks>
public enum DebugEventDecision
{
	/// <summary>
	///     Lets Cheat Engine handle and continue the event. The native callback returns zero and no SDK continuation is
	///     invoked.
	/// </summary>
	ContinueWithCheatEngine = 0,

	/// <summary>
	///     Requests plugin-owned continuation. Unsupported by the current profile, so the dispatcher records the
	///     request and safely returns <see cref="ContinueWithCheatEngine" /> instead of claiming ownership it cannot
	///     discharge.
	/// </summary>
	PluginOwnsContinuation = 1
}
