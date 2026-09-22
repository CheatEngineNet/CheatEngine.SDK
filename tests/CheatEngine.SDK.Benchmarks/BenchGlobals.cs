using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Benchmarks;

/// <summary>
///     A generated <c>[LuaGlobal]</c> binding over a plain Lua function (defined by <see cref="GlobalCallBenchmarks" />
///     in its <c>[GlobalSetup]</c>): the real generated call shape, exercised with two arguments and one result.
/// </summary>
internal static partial class BenchGlobals
{
	/// <summary>Calls the Lua global <c>cheatengine_sdk_bench_add</c>: two arguments, one result.</summary>
	/// <param name="a">First addend.</param>
	/// <param name="b">Second addend.</param>
	/// <returns>The sum.</returns>
	/// <exception cref="LuaException">The global is missing, the call raised, or the result is not an integer.</exception>
	[LuaGlobal("cheatengine_sdk_bench_add")]
	public static partial long Add(long a, long b);
}
