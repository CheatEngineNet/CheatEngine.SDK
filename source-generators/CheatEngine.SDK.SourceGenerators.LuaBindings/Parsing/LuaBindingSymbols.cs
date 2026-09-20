using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>
///     Resolves generator inputs to the real SDK symbols in the consumer compilation. A metadata name is only a lookup
///     key: matching it structurally would let an unrelated source or referenced assembly impersonate an SDK annotation
///     or <c>LuaState</c>.
/// </summary>
internal static class LuaBindingSymbols
{
    private const string AnnotationsAssemblyName = "CheatEngine.SDK.Annotations";
    private const string LuaAssemblyName = "CheatEngine.SDK.Lua";
    private const string LuaStateMetadataName = "CheatEngine.SDK.Lua.State.LuaState";

    /// <summary>
    ///     Returns whether one attribute in <paramref name="attributes" /> is exactly the SDK annotation named by
    ///     <paramref name="metadataName" /> in <paramref name="compilation" />.
    /// </summary>
    public static bool ContainsSdkAttribute(ImmutableArray<AttributeData> attributes, Compilation compilation,
        string metadataName)
    {
        var expected = ResolveSdkType(compilation, metadataName, AnnotationsAssemblyName);
        if (expected is null) return false;

        foreach (var attribute in attributes)
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected))
                return true;

        return false;
    }

    /// <summary>Gets the actual <c>LuaState</c> symbol, or <see langword="null" /> when the SDK runtime is absent.</summary>
    public static INamedTypeSymbol? ResolveLuaState(Compilation compilation)
    {
        return ResolveSdkType(compilation, LuaStateMetadataName, LuaAssemblyName);
    }

    /// <summary>
    ///     Reads the name argument belonging to the resolved SDK attribute. An unrelated attribute with the same
    ///     metadata name is ignored even when Roslyn's discovery predicate delivered it.
    /// </summary>
    public static string? ReadSdkAttributeName(ImmutableArray<AttributeData> attributes, Compilation compilation,
        string metadataName)
    {
        var expected = ResolveSdkType(compilation, metadataName, AnnotationsAssemblyName);
        if (expected is null) return null;

        foreach (var attribute in attributes)
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected)) continue;

            var arguments = attribute.ConstructorArguments;
            return arguments.Length == 1 && arguments[0] is { Kind: TypedConstantKind.Primitive, Value: string name }
                ? name
                : null;
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveSdkType(Compilation compilation, string metadataName, string assemblyName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        return type is not null && string.Equals(type.ContainingAssembly.Name, assemblyName, StringComparison.Ordinal)
            ? type
            : null;
    }
}
