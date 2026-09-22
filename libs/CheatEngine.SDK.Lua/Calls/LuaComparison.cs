using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Calls;

/// <summary>
///     The comparison performed by <see cref="LuaState.TryCompare" />. The numeric values are those of the Lua 5.3 C API
///     (<c>LUA_OPEQ</c>, <c>LUA_OPLT</c>, <c>LUA_OPLE</c>) and of the helper chunk that implements the comparison.
/// </summary>
public enum LuaComparison
{
	/// <summary><c>a == b</c>, honouring <c>__eq</c>.</summary>
	Equal = 0,

	/// <summary><c>a &lt; b</c>, honouring <c>__lt</c>.</summary>
	Less = 1,

	/// <summary><c>a &lt;= b</c>, honouring <c>__le</c>.</summary>
	LessOrEqual = 2
}
