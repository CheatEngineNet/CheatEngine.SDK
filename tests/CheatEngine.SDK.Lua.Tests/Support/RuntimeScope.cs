using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Support;

/// <summary>
///     Attaches <see cref="LuaRuntime" /> to a fixture state through <see cref="HostDouble" /> for the duration of a test
///     and detaches on dispose, so that no test leaves the process-wide runtime attached.
/// </summary>
internal sealed unsafe class RuntimeScope : IDisposable
{
	/// <param name="state">The fixture state the host double provides to the attaching (main) thread.</param>
	/// <param name="withPusher">Whether the double's binding includes a host-object pusher.</param>
	/// <param name="admitWorkerThreads">
	///     When <see langword="true" />, calls the experimental <see cref="LuaRuntime.AdmitWorkerThreads" /> right after
	///     attaching, for tests that acquire a Lua operation from a worker thread or from an xUnit v3 async continuation
	///     that may resume on one. Every call site that passes this flag carries its own file-level
	///     <c>#pragma warning disable CESDK5001</c> with a justification comment: never a project-wide <c>NoWarn</c>.
	/// </param>
	public RuntimeScope(NativeLuaState state, bool withPusher = true, bool admitWorkerThreads = false)
	{
		Binding = HostDouble.CreateBinding(state.L, withPusher);
		LuaRuntime.Attach(Binding);
		if (admitWorkerThreads)
		{
#pragma warning disable CESDK5001 // Test-only opt-in: the caller passed admitWorkerThreads: true for a worker-thread
			// or post-await scenario; every such call site carries its own justification (see individual test files).
			LuaRuntime.AdmitWorkerThreads();
#pragma warning restore CESDK5001
		}
	}

	public LuaHostBinding Binding
	{
		get;
	}

	public void Dispose()
	{
		LuaRuntime.Detach();
	}
}
