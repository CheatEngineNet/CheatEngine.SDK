using System.Collections.Concurrent;
using CESDK.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CESDK.Analyzers.Plugin;

/// <summary>
///     What <see cref="CheatEnginePluginAnalyzer" /> learns about one compilation and can only judge once the whole
///     compilation has been seen: the plugin classes (CESDK0002) and the namespace declarations under <c>CESDK</c>
///     (CESDK0004).
/// </summary>
/// <remarks>
///     One instance per compilation, created in the compilation-start action and captured by the actions of that
///     compilation only, never stored in an analyzer field (RS1008). Thread safety: the symbol and syntax actions add
///     concurrently; the compilation-end action runs after all of them and is the only reader.
/// </remarks>
internal sealed class PluginCompilationState
{
    private readonly bool _entryPointIsGenerated;
    private readonly ConcurrentQueue<(string Name, Location Location)> _pluginClasses = new();
    private readonly ConcurrentQueue<(string Name, Location Location)> _reservedNamespaces = new();

    /// <param name="entryPointIsGenerated">
    ///     <see langword="false" /> when the project switched the generated entry point off: choosing between several
    ///     plugin classes is then the author's own bootstrap code's business and CESDK0002 is not reported.
    /// </param>
    public PluginCompilationState(bool entryPointIsGenerated)
    {
        _entryPointIsGenerated = entryPointIsGenerated;
    }

    /// <summary>Records a class that carries the plugin attribute, valid or not.</summary>
    public void AddPluginClass(string name, Location location)
    {
        _pluginClasses.Enqueue((name, location));
    }

    /// <summary>Records a top-level namespace declaration that is <c>CESDK</c> or starts with <c>CESDK.</c>.</summary>
    public void AddReservedNamespace(string name, Location location)
    {
        _reservedNamespaces.Enqueue((name, location));
    }

    /// <summary>The compilation-end action: reports CESDK0002 and CESDK0004.</summary>
    public void Report(CompilationAnalysisContext context)
    {
        var pluginClassCount = _pluginClasses.Count;
        if (pluginClassCount == 0)
            // Not a plugin assembly (the SDK's own libraries, a helper library): both rules are about plugins.
            return;

        if (pluginClassCount > 1 && _entryPointIsGenerated)
            foreach (var (name, location) in _pluginClasses)
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MultiplePluginClasses, location, name,
                    pluginClassCount));

        foreach (var (name, location) in _reservedNamespaces)
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ReservedNamespace, location, name));
    }
}
