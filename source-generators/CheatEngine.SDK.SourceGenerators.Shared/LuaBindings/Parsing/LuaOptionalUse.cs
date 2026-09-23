namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>How a binding signature uses <c>LuaOptional&lt;T&gt;</c> at one parameter, result or return position.</summary>
internal enum LuaOptionalUse
{
	/// <summary>The type is not a <c>LuaOptional&lt;T&gt;</c> of any origin.</summary>
	NotOptional,

	/// <summary>The resolved SDK <c>LuaOptional&lt;T&gt;</c> of a built-in kind that can be optional.</summary>
	Supported,

	/// <summary>
	///     The resolved SDK <c>LuaOptional&lt;T&gt;</c>, but <c>T</c> cannot be optional here: <c>string?</c>, a
	///     custom-marshalled or unmarshalled type, or a nested <c>LuaOptional</c>.
	/// </summary>
	Unsupported,

	/// <summary>A type with the SDK's <c>LuaOptional`1</c> metadata name that is not the <c>CheatEngine.SDK.Lua</c> type.</summary>
	LookAlike
}
