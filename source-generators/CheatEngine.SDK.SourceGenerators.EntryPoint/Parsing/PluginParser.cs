using System.Threading;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Model;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Parsing;

/// <summary>
///     The <c>ForAttributeWithMetadataName</c> transform: the only place of the pipeline that touches symbols. It
///     reduces the attributed class to a <see cref="PluginModel" /> and lets go of everything else.
/// </summary>
internal static class PluginParser
{
    /// <summary>Builds the model of one attributed class.</summary>
    public static PluginModel Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var type = (INamedTypeSymbol)context.TargetSymbol;
        var compilation = context.SemanticModel.Compilation;
        var symbols = EntryPointContractSymbols.Resolve(compilation);
        var attribute = FindAttribute(context.Attributes, symbols.PluginAttribute);

        var issues = PluginShape.Inspect(
            type,
            attribute,
            symbols.PluginBase,
            symbols.SetsRequiredMembersAttribute,
            symbols.ObsoleteAttribute,
            out var displayName,
            out var parameterlessConstructor);

        // FullyQualifiedFormat: 'global::' prefix, containing types, escaped keyword identifiers. Identifiers come
        // out as declared (a non-ASCII letter is not turned into a \uXXXX escape): the generated file is UTF-8.
        return new PluginModel(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            displayName,
            EntryPointDeclaredDiagnosticIds.Collect(
                type,
                parameterlessConstructor,
                symbols.ExperimentalAttribute,
                symbols.ObsoleteAttribute),
            issues);
    }

    private static AttributeData? FindAttribute(
        System.Collections.Immutable.ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol? pluginAttribute)
    {
        if (pluginAttribute is null) return null;

        foreach (var attribute in attributes)
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, pluginAttribute))
                return attribute;

        return null;
    }
}
