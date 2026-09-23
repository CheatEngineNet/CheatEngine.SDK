namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>The SDK's policy for admitting a Lua operation on a thread other than the host's captured main thread.</summary>
/// <remarks>
///     <para>
///         This is a lifecycle-admission policy, not a claim about the shared Lua heap: Cheat Engine hands out one Lua
///         thread per OS thread, and distinct <c>lua_State*</c> values can be coroutines of one universe (ADR-07). The
///         policy decides only whether this SDK copy starts new Lua work on a given thread; it never infers, and never
///         provides, cross-thread serialization of that shared universe.
///     </para>
///     <para>
///         <see cref="MainThreadOnly" /> is the conservative 2.0 default: <see cref="LuaRuntime.ThreadAdmission" /> reads
///         it, and every <see cref="LuaRuntime" /> attach or detach restores it. Read the policy freely; only
///         <see cref="LuaRuntime.AdmitWorkerThreads" /> is gated, because opting into <see cref="WorkerThreads" /> is the
///         action with an unqualified consequence (Q19), not observing which policy is active.
///     </para>
/// </remarks>
public enum LuaThreadAdmission
{
	/// <summary>
	///     The conservative default: a new <see cref="LuaRuntime.AcquireOperation()" />-family call is admitted only on
	///     the host's captured main thread, inside a host-invoked callback already running on the calling thread, or
	///     through the single documented <c>synchronize</c> hand-off (
	///     <see cref="LuaRuntime.AcquireOperationForMainThreadDispatch" />).
	/// </summary>
	MainThreadOnly = 0,

	/// <summary>
	///     Set only by <see cref="LuaRuntime.AdmitWorkerThreads" />: a worker thread that is not already running admitted
	///     Lua work is also admitted. Unqualified until Q19 passes at C3 and C4 (ADR-07).
	/// </summary>
	WorkerThreads = 1
}
