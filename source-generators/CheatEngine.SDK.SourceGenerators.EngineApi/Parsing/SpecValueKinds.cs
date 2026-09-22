using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Parsing;

/// <summary>
///     The spec file's kind tokens (lower-case, hand-typed by a curator) and their mapping to <see cref="LuaValueKind" />.
/// </summary>
/// <remarks>
///     One token per <see cref="LuaValueKind" /> member, plus <c>string?</c> for the nullable string variant (the only
///     kind whose C# spelling depends on a flag, <c>LuaValueKinds.TypeName</c>'s <c>isNullable</c> parameter). Adding a
///     spec-file kind is only ever "add a case here", never a change to
///     <c>CheatEngine.SDK.SourceGenerators.Shared.LuaEmit</c>: the
///     vocabulary is fixed
///     by the marshallers <c>CheatEngine.SDK.Lua</c> ships, and this table merely names it for hand-curated text.
/// </remarks>
internal static class SpecValueKinds
{
	/// <summary>
	///     Parses one kind token; <see langword="false" /> for anything else, <paramref name="kind" />/
	///     <paramref name="isNullable" /> then undefined.
	/// </summary>
	public static bool TryParse(string token, out LuaValueKind kind, out bool isNullable)
	{
		switch (token)
		{
			case "int32":
				kind = LuaValueKind.Int32;
				isNullable = false;
				return true;
			case "int64":
				kind = LuaValueKind.Int64;
				isNullable = false;
				return true;
			case "single":
				kind = LuaValueKind.Single;
				isNullable = false;
				return true;
			case "double":
				kind = LuaValueKind.Double;
				isNullable = false;
				return true;
			case "boolean":
				kind = LuaValueKind.Boolean;
				isNullable = false;
				return true;
			case "address":
				kind = LuaValueKind.Address;
				isNullable = false;
				return true;
			case "utf8":
				kind = LuaValueKind.Utf8;
				isNullable = false;
				return true;
			case "string":
				kind = LuaValueKind.String;
				isNullable = false;
				return true;
			case "string?":
				kind = LuaValueKind.String;
				isNullable = true;
				return true;
			default:
				kind = default;
				isNullable = false;
				return false;
		}
	}
}
