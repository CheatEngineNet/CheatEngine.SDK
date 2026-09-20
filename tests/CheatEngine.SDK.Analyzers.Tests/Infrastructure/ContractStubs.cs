using System.Collections.Immutable;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     Metadata references for the cross-project SDK contracts the rules are driven by: the plugin attribute from
///     <c>CheatEngine.SDK.Annotations</c> and the base class from <c>CheatEngine.SDK.Hosting</c>.
/// </summary>
/// <remarks>
///     The references intentionally use the shipping assemblies, rather than source declarations with matching names.
///     This lets the analyzers prove symbol identity including the defining assembly and catches a lookalike attribute or
///     host base exactly as a plugin compilation would. The namespace <c>CheatEngine.SDK</c> is not reserved;
///     CESDK0004 reserves only <c>CESDK</c>, the namespace of the <c>CESDK.CESDK</c> bootstrap type.
/// </remarks>
internal static class ContractStubs
{
    /// <summary>The real assembly references that a consumer receives from direct SDK package references.</summary>
    public static ImmutableArray<MetadataReference> References { get; } =
    [
        MetadataReference.CreateFromFile(typeof(CheatEnginePluginAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CheatEnginePlugin).Assembly.Location),
    ];

    /// <summary>Adds the real SDK contract metadata to <paramref name="state" />.</summary>
    public static void AddTo(SolutionState state)
    {
        state.AdditionalReferences.AddRange(References);
    }
}
