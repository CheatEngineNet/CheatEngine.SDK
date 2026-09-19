using System;
using CESDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;

namespace CESDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Maps a type symbol to the <see cref="LuaValueKind" /> a marshaller of <c>CESDK.Lua</c> handles, and recognises
///     the three other types the binding shapes know: <c>CESDK.Lua.State.LuaState</c>, <c>ReadOnlySpan&lt;byte&gt;</c> and
///     <c>Span&lt;byte&gt;</c>. Part of the shape-validation source that the CESDK2xxx analyzer links.
/// </summary>
/// <remarks>
///     Types are recognised by special type or by name and namespace while looking at the symbol alone, never through
///     <c>Compilation.GetTypeByMetadataName</c> (which returns <see langword="null" /> when two references define a
///     type), so that the decision depends on nothing but the symbol.
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

    /// <summary>Whether <paramref name="type" /> is <c>CESDK.Lua.State.LuaState</c>.</summary>
    public static bool IsLuaState(ITypeSymbol type)
    {
        return type is INamedTypeSymbol
        {
            Name: "LuaState", Arity: 0, ContainingType: null, ContainingNamespace:
            {
                Name: "State",
                ContainingNamespace:
                {
                    Name: "Lua",
                    ContainingNamespace: { Name: "CESDK", ContainingNamespace.IsGlobalNamespace: true }
                }
            }
        };
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
