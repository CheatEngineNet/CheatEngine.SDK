using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

/// <summary>The classified signature of a <c>[LuaFunction]</c> method, as far as the shape inspection got.</summary>
/// <param name="PassesState">The first parameter is a <c>LuaState</c> that receives the thunk's state.</param>
/// <param name="Arguments">The Lua arguments, in order.</param>
/// <param name="ReturnKind">The kind pushed as the result, or <see langword="null" /> for <see langword="void" />.</param>
/// <param name="ReturnMarshaller">An explicit static marshaller for the result, or <see langword="null" />.</param>
internal readonly record struct LuaFunctionSignature(
	bool PassesState,
	EquatableArray<LuaArgumentModel> Arguments,
	LuaValueKind? ReturnKind,
	LuaCustomMarshallerModel? ReturnMarshaller = null)
{
	/// <summary>Initializes a built-in scalar signature with the pre-custom-marshaller binary shape.</summary>
	public LuaFunctionSignature(bool passesState, EquatableArray<LuaArgumentModel> arguments,
		LuaValueKind? returnKind)
		: this(passesState, arguments, returnKind, null)
	{
	}
}
