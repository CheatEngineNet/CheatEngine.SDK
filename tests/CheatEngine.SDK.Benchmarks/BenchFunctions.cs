using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.SDK.Benchmarks;

/// <summary>
///     A generated <c>[LuaFunction]</c> thunk, registered once by <see cref="CallbackBenchmarks" /> and then called
///     from a Lua loop many times inside one protected call: register once, call from Lua N times.
/// </summary>
internal static partial class BenchFunctions
{
	/// <summary>Lua: <c>cheatengine_sdk_bench_touch(x)</c>. Returns <paramref name="x" /> plus one.</summary>
	/// <param name="x">The running value.</param>
	/// <returns><paramref name="x" /> + 1.</returns>
	[LuaFunction("cheatengine_sdk_bench_touch")]
	public static long Touch(long x)
	{
		return x + 1;
	}
}
