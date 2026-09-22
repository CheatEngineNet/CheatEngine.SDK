using System.ComponentModel;

namespace CheatEngine.SDK.Lua.CompilerServices;

/// <summary>Outcome of resolving a Lua global function for SDK-internal failure classification.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum LuaGlobalPushStatus
{
	/// <summary>The function was pushed.</summary>
	Success,

	/// <summary>The global was absent or was not a function.</summary>
	Unavailable,

	/// <summary>A protected global lookup or reference creation failed.</summary>
	LuaFailure
}
