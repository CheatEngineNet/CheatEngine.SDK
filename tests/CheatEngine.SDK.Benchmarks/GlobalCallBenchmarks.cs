using BenchmarkDotNet.Attributes;

using CheatEngine.SDK.Benchmarks.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Benchmarks;

/// <summary>
///     A protected global call with two arguments and one result, through the real generated call shape
///     (<see cref="BenchGlobals.Add(long, long)" />).
/// </summary>
[MemoryDiagnoser(false)]
[BenchmarkCategory("GlobalCall")]
public class GlobalCallBenchmarks : IDisposable
{
	private NativeLuaState? _state;

	/// <inheritdoc />
	public void Dispose()
	{
		LuaRuntime.Detach();
		_state?.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>Opens a state, attaches the ambient runtime to it and defines the Lua-side <c>cheatengine_sdk_bench_add</c>.</summary>
	[GlobalSetup]
	public void Setup()
	{
		NativeLuaLibrary.ThrowIfUnavailable();
		_state = new NativeLuaState();
		LuaState l = FakeHostRuntime.Attach(_state, false);
		LuaStatus defined = l.TryExecute("function cheatengine_sdk_bench_add(a, b) return a + b end"u8, 0);
		if (!defined.IsOk)
		{
			throw new InvalidOperationException("Defining cheatengine_sdk_bench_add failed: " +
												LuaError.FromStack(l, defined));
		}
	}

	/// <summary>
	///     Detaches the ambient runtime and closes the state. BenchmarkDotNet does not call <see cref="Dispose" />
	///     itself; this is what <c>[GlobalCleanup]</c> is for.
	/// </summary>
	[GlobalCleanup]
	public void Cleanup()
	{
		Dispose();
	}

	/// <summary>
	///     The generated wrapper's hot path: provider, <c>gettop</c>, cached-global push, two argument pushes,
	///     <c>pcallk</c>, read, <c>settop</c>.
	/// </summary>
	[Benchmark]
	public long ProtectedGlobalCall()
	{
		return BenchGlobals.Add(19, 23);
	}
}
