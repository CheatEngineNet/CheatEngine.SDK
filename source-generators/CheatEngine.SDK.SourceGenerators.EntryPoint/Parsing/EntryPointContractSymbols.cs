using System;
using CheatEngine.SDK.SourceGenerators.Shared;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Parsing;

/// <summary>
///     The actual contract symbols an entry-point generator invocation accepts. Metadata names are useful for the
///     syntax-provider index, but they are not sufficient to prove that an attributed class uses this SDK rather than a
///     same-named type from another reference.
/// </summary>
/// <remarks>
///     The SDK contracts live in separate assemblies. Looking each one up through its referenced assembly symbol avoids
///     <see cref="Compilation.GetTypeByMetadataName" /> choosing a source or foreign-reference look-alike before the
///     SDK reference. The symbols are transient parsing data only; no Roslyn object escapes into an incremental value.
/// </remarks>
internal readonly struct EntryPointContractSymbols
{
    private const string AnnotationsAssemblyName = "CheatEngine.SDK.Annotations";
    private const string HostingAssemblyName = "CheatEngine.SDK.Hosting";

    private const string SetsRequiredMembersAttributeMetadataName =
        "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    private const string ObsoleteAttributeMetadataName = "System.ObsoleteAttribute";
    private const string ExperimentalAttributeMetadataName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

    private EntryPointContractSymbols(
        INamedTypeSymbol? pluginAttribute,
        INamedTypeSymbol? pluginBase,
        INamedTypeSymbol? setsRequiredMembersAttribute,
        INamedTypeSymbol? obsoleteAttribute,
        INamedTypeSymbol? experimentalAttribute)
    {
        PluginAttribute = pluginAttribute;
        PluginBase = pluginBase;
        SetsRequiredMembersAttribute = setsRequiredMembersAttribute;
        ObsoleteAttribute = obsoleteAttribute;
        ExperimentalAttribute = experimentalAttribute;
    }

    /// <summary>The actual <c>CheatEnginePluginAttribute</c> symbol from <c>CheatEngine.SDK.Annotations</c>.</summary>
    public INamedTypeSymbol? PluginAttribute { get; }

    /// <summary>The actual <c>CheatEnginePlugin</c> symbol from <c>CheatEngine.SDK.Hosting</c>.</summary>
    public INamedTypeSymbol? PluginBase { get; }

    /// <summary>The BCL marker that permits a selected constructor to satisfy required members.</summary>
    public INamedTypeSymbol? SetsRequiredMembersAttribute { get; }

    /// <summary>The BCL attribute that can make construction an unsuppressible error.</summary>
    public INamedTypeSymbol? ObsoleteAttribute { get; }

    /// <summary>The BCL attribute whose caller-supplied diagnostic ID must be disabled in generated code.</summary>
    public INamedTypeSymbol? ExperimentalAttribute { get; }

    /// <summary>Resolves the SDK contract and BCL symbols for one source-generator transform.</summary>
    public static EntryPointContractSymbols Resolve(Compilation compilation)
    {
        return new EntryPointContractSymbols(
            FindReferencedType(
                compilation,
                AnnotationsAssemblyName,
                AnnotationsMetadataNames.CheatEnginePluginAttribute),
            FindReferencedType(compilation, HostingAssemblyName, AnnotationsMetadataNames.CheatEnginePluginBase),
            compilation.GetTypeByMetadataName(SetsRequiredMembersAttributeMetadataName),
            compilation.GetTypeByMetadataName(ObsoleteAttributeMetadataName),
            compilation.GetTypeByMetadataName(ExperimentalAttributeMetadataName));
    }

    private static INamedTypeSymbol? FindReferencedType(
        Compilation compilation,
        string assemblyName,
        string metadataName)
    {
        foreach (var reference in compilation.References)
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly
                && string.Equals(assembly.Identity.Name, assemblyName, StringComparison.Ordinal))
                return assembly.GetTypeByMetadataName(metadataName);

        return null;
    }
}
