namespace CheatEngine.SDK.Repository.Tests.LuaBridge;

/// <summary>One raw use of a <c>LuaApi</c> member found in a production source.</summary>
/// <param name="Member">The member name, for example <c>lua_tolstring</c>.</param>
/// <param name="Line">The 1-based line of the use.</param>
internal readonly record struct LuaApiUse(string Member, int Line);
