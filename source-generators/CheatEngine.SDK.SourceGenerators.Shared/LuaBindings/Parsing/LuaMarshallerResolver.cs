using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Resolves an explicit SDK <c>[LuaMarshaller(typeof(TMarshaller))]</c> to a value-only emission model. Both the
///     annotation and <c>ILuaMarshaller&lt;T&gt;</c> are supplied as resolved SDK symbols, so a source declaration with
///     the same metadata name cannot impersonate either contract.
/// </summary>
internal static class LuaMarshallerResolver
{
    /// <summary>
    ///     Gets the explicit marshaller model for <paramref name="valueType" />, or reports whether an annotation was
    ///     present but invalid. A missing annotation is not an error and leaves <paramref name="marshaller" /> null.
    /// </summary>
    public static bool TryResolve(ITypeSymbol valueType, ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol? marshallerAttribute, INamedTypeSymbol? marshallerContract,
        out LuaCustomMarshallerModel? marshaller, out bool hasAttribute)
    {
        marshaller = null;
        hasAttribute = false;
        if (marshallerAttribute is null || marshallerContract is null) return true;

        AttributeData? attribute = null;
        foreach (var candidate in attributes)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, marshallerAttribute)) continue;

            attribute = candidate;
            break;
        }

        if (attribute is null) return true;
        hasAttribute = true;

        var arguments = attribute.ConstructorArguments;
        if (arguments.Length != 1 || arguments[0] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol type })
            return false;

        if (!Implements(type, marshallerContract, valueType)) return false;

        var valueTypeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var marshallerTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        marshaller = new LuaCustomMarshallerModel(valueTypeName, marshallerTypeName, "value", valueType.IsReferenceType);
        return true;
    }

    private static bool Implements(INamedTypeSymbol candidate, INamedTypeSymbol contract, ITypeSymbol valueType)
    {
        foreach (var implementation in candidate.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, contract)
                || implementation.TypeArguments.Length != 1)
                continue;

            if (SymbolEqualityComparer.Default.Equals(implementation.TypeArguments[0], valueType)) return true;
        }

        return false;
    }
}
