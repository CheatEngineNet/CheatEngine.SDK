using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>One generated instance-method body on a <c>[LuaClass]</c> borrowed handle.</summary>
internal sealed record LuaObjectMethodModel(
	ContainingTypeModel ContainingType,
	string LuaName,
	string Modifiers,
	string MethodName,
	EquatableArray<LuaArgumentModel> Arguments,
	LuaCallForm Form,
	EquatableArray<LuaResultModel> Results,
	LuaValueKind? ReturnKind,
	bool ReturnIsNullable,
	string SortKey,
	bool IsValid)
{
	/// <summary>Number of values the protected call keeps.</summary>
	public int ResultCount => Form == LuaCallForm.Try ? Results.Length : ReturnKind is null ? 0 : 1;
}
