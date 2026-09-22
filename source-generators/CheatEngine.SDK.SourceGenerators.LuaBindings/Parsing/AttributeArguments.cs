using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>Reads the one constructor argument of <c>[LuaFunction(name)]</c> and <c>[LuaGlobal(name)]</c>.</summary>
internal static class AttributeArguments
{
	/// <summary>
	///     The name argument of the first attribute, or <see langword="null" /> when it is missing, not a string or
	///     <see langword="null" /> (all of which happen while the author is typing). The attribute constructor's own
	///     check never runs at compile time, so an empty string reaches here too and fails the name rule later.
	/// </summary>
	public static string? ReadName(ImmutableArray<AttributeData> attributes)
	{
		if (attributes.IsDefaultOrEmpty)
		{
			return null;
		}

		ImmutableArray<TypedConstant> arguments = attributes[0].ConstructorArguments;
		return arguments.Length == 1 && arguments[0] is { Kind: TypedConstantKind.Primitive, Value: string name }
			? name
			: null;
	}
}
