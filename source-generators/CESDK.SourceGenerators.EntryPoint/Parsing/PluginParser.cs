using System.Threading;
using CESDK.SourceGenerators.EntryPoint.Model;
using CESDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis;

namespace CESDK.SourceGenerators.EntryPoint.Parsing;

/// <summary>
///     The <c>ForAttributeWithMetadataName</c> transform: the only place of the pipeline that touches symbols. It
///     reduces the attributed class to a <see cref="PluginModel" /> and lets go of everything else.
/// </summary>
internal static class PluginParser
{
    // Metadata names of the two optional BCL attributes the shape check needs. They are resolved per node, from the
    // semantic model the transform already has, rather than combined with the whole compilation (which the
    // plugin-list step already collapses to a value before anything reruns from it).
    private const string SetsRequiredMembersAttributeMetadataName =
        "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    private const string ObsoleteAttributeMetadataName = "System.ObsoleteAttribute";

    /// <summary>Builds the model of one attributed class.</summary>
    public static PluginModel Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var type = (INamedTypeSymbol)context.TargetSymbol;
        var compilation = context.SemanticModel.Compilation;
        var attribute = context.Attributes.IsDefaultOrEmpty ? null : context.Attributes[0];

        var issues = PluginShape.Inspect(
            type,
            attribute,
            compilation.GetTypeByMetadataName(SetsRequiredMembersAttributeMetadataName),
            compilation.GetTypeByMetadataName(ObsoleteAttributeMetadataName),
            out var displayName);

        // FullyQualifiedFormat: 'global::' prefix, containing types, escaped keyword identifiers. Identifiers come
        // out as declared (a non-ASCII letter is not turned into a \uXXXX escape): the generated file is UTF-8.
        return new PluginModel(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            displayName,
            EntryPointDeclaredDiagnosticIds.Collect(type),
            issues);
    }
}
