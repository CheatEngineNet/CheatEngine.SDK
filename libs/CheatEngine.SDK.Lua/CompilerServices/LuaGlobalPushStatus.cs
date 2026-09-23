using System.ComponentModel;

namespace CheatEngine.SDK.Lua.CompilerServices;

/// <summary>Outcome of resolving a Lua global function for SDK-internal failure classification.</summary>
/// <remarks>
///     The zero value is <see cref="Unknown" />, never <see cref="Success" />: a resolution that was never performed
///     cannot read as a pushed function. The numeric values are part of the contract and never change.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum LuaGlobalPushStatus
{
	/// <summary>
	///     No resolution was recorded: the value of <c>default(LuaGlobalPushOutcome)</c>. Never success, and nothing
	///     was pushed.
	/// </summary>
	Unknown = 0,

	/// <summary>The function was pushed.</summary>
	Success = 1,

	/// <summary>The global was absent or was not a function.</summary>
	Unavailable = 2,

	/// <summary>A protected global lookup or reference creation failed.</summary>
	LuaFailure = 3
}
