namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     A one-shot fact <see cref="LuaRuntime" /> reports through <see cref="LuaRuntime.DiagnosticObserver" />, at most
///     once per attachment, always outside every runtime lock. <c>CheatEngine.SDK.Hosting</c> is the only subscriber:
///     it turns each kind into one stable-category <c>HostLog</c> entry (A24: never localized, never keyed off CE's UI
///     language).
/// </summary>
internal enum LuaRuntimeDiagnostic
{
	/// <summary>
	///     A worker thread's <see cref="LuaRuntime.AcquireOperation()" />-family call was refused by the conservative
	///     default before the host's state provider ran. Hosting logs this as <c>LuaWorkerThreadRefused:</c>.
	/// </summary>
	WorkerThreadRefused,

	/// <summary>
	///     <see cref="LuaRuntime.ExternalStateResetDetected" /> just became <see langword="true" />. Hosting logs this
	///     as <c>LuaStateReplacedExternally:</c>.
	/// </summary>
	ExternalStateReset
}
