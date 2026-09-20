using CheatEngine.SDK.SourceGenerators.Shared;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Model;

/// <summary>The MSBuild switches the generator honours, reduced to values.</summary>
/// <param name="GenerateEntryPoint">
///     <c>CheatEngineSdkGenerateEntryPoint</c>: <see langword="false" /> switches the generator off (for a plugin author
///     who writes
///     <c>CESDK.CESDK</c> by hand). Defaults to <see langword="false" /> when the property is not compiler-visible, so an
///     indirect package reference cannot generate a bootstrap in a consuming project.
/// </param>
internal readonly record struct EntryPointOptions(bool GenerateEntryPoint)
{
    /// <summary>Global analyzer-config key of the switch.</summary>
    public const string GenerateEntryPointKey = BuildProperty.KeyPrefix + "CheatEngineSdkGenerateEntryPoint";

    /// <summary>Reads the switches from <c>AnalyzerConfigOptionsProvider.GlobalOptions</c>.</summary>
    public static EntryPointOptions From(AnalyzerConfigOptions globalOptions)
    {
        return new EntryPointOptions(BuildProperty.ReadBoolean(globalOptions, GenerateEntryPointKey, false));
    }
}
