using System;

using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Maps a type symbol to the <see cref="LuaValueKind" /> a marshaller of <c>CheatEngine.SDK.Lua</c> handles, and
///     recognises
///     the three other types the binding shapes know: <c>CheatEngine.SDK.Lua.State.LuaState</c>,
///     <c>ReadOnlySpan&lt;byte&gt;</c> and
///     <c>Span&lt;byte&gt;</c>. Part of the shape-validation source that the CESDK2xxx analyzer links.
/// </summary>
/// <remarks>
///     Scalar values and spans are recognised from their language/runtime symbols. <c>LuaState</c> is different: it is
///     accepted only when it is the symbol resolved from the actual SDK Lua assembly. A matching namespace and type
///     name in the consumer's source or another assembly is never a runtime capability.
/// </remarks>
internal static class LuaValueKindMapper
{
	/// <summary>
	///     Classifies <paramref name="type" />. <paramref name="isNullable" /> is <see langword="true" /> for
	///     <c>string?</c>; a nullable value type (<c>int?</c>) is not a supported kind.
	/// </summary>
	public static bool TryMap(ITypeSymbol type, out LuaValueKind kind, out bool isNullable)
	{
		isNullable = false;
		if (type is null)
		{
			kind = default;
			return false;
		}

		switch (type.SpecialType)
		{
			case SpecialType.System_Int32:
				kind = LuaValueKind.Int32;
				return true;
			case SpecialType.System_Int64:
				kind = LuaValueKind.Int64;
				return true;
			case SpecialType.System_Single:
				kind = LuaValueKind.Single;
				return true;
			case SpecialType.System_Double:
				kind = LuaValueKind.Double;
				return true;
			case SpecialType.System_Boolean:
				kind = LuaValueKind.Boolean;
				return true;
			case SpecialType.System_UIntPtr:
				kind = LuaValueKind.Address;
				return true;
			case SpecialType.System_String:
				kind = LuaValueKind.String;
				isNullable = type.NullableAnnotation == NullableAnnotation.Annotated;
				return true;
		}

		if (IsReadOnlySpanOfByte(type))
		{
			kind = LuaValueKind.Utf8;
			return true;
		}

		kind = default;
		return false;
	}

	/// <summary>Whether <paramref name="type" /> is the resolved SDK <c>LuaState</c> symbol.</summary>
	public static bool IsLuaState(ITypeSymbol type, INamedTypeSymbol? expectedLuaState)
	{
		return expectedLuaState is not null && SymbolEqualityComparer.Default.Equals(type, expectedLuaState);
	}

	/// <summary>Whether <paramref name="type" /> is <c>System.ReadOnlySpan&lt;byte&gt;</c>.</summary>
	public static bool IsReadOnlySpanOfByte(ITypeSymbol type)
	{
		return IsSystemSpanOfByte(type, "ReadOnlySpan");
	}

	/// <summary>Whether <paramref name="type" /> is <c>System.Span&lt;byte&gt;</c>.</summary>
	public static bool IsSpanOfByte(ITypeSymbol type)
	{
		return IsSystemSpanOfByte(type, "Span");
	}

	private static bool IsSystemSpanOfByte(ITypeSymbol type, string name)
	{
		return type is INamedTypeSymbol { Arity: 1, ContainingType: null } named
			   && string.Equals(named.Name, name, StringComparison.Ordinal)
			   && named.TypeArguments[0].SpecialType == SpecialType.System_Byte
			   && named.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };
	}
}
