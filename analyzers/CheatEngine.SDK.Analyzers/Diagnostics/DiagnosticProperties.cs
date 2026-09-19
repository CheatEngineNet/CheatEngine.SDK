using CheatEngine.SDK.SourceGenerators.Shared.Shapes;

namespace CheatEngine.SDK.Analyzers.Diagnostics;

/// <summary>
///     Keys of <see cref="Microsoft.CodeAnalysis.Diagnostic.Properties" /> through which an analyzer tells its code fix
///     what it found, so that the fix never has to repeat the analysis.
/// </summary>
internal static class DiagnosticProperties
{
    /// <summary>
    ///     On CESDK0001: the name of the single <see cref="PluginShapeIssues" /> flag the
    ///     diagnostic is about.
    /// </summary>
    public const string PluginClassProblem = "CheatEngine.SDK.PluginClassProblem";
}
