namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     One explicit <c>[LuaMarshaller]</c> selection after the generator has verified that the named type implements
///     <c>ILuaMarshaller&lt;T&gt;</c> for the annotated value. The strings are already globally-qualified C# source
///     spellings, so emitters never need to retain a Roslyn symbol or use reflection at run time.
/// </summary>
/// <param name="ValueTypeName">The annotated managed value type as generated code must spell it.</param>
/// <param name="MarshallerTypeName">The concrete marshaller type whose static methods generated code calls.</param>
/// <param name="ExpectedTypeName">A compact Lua-facing type name for a generated failure message.</param>
/// <param name="IsReferenceType">Whether defaulting the value requires the null-forgiving operator.</param>
internal sealed record LuaCustomMarshallerModel(
	string ValueTypeName,
	string MarshallerTypeName,
	string ExpectedTypeName,
	bool IsReferenceType);
