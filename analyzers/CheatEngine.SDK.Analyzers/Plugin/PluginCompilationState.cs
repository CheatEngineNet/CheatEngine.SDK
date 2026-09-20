using System.Collections.Concurrent;
using CheatEngine.SDK.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Plugin;

/// <summary>
///     What <see cref="CheatEnginePluginAnalyzer" /> learns about one compilation and can only judge once the whole
///     compilation has been seen: plugin classes (CESDK0002), manual bootstrap (CESDK0003), namespace declarations
///     under <c>CESDK</c> (CESDK0004), and source declarations of the host-mandated <c>CESDK.CESDK</c> type (CESDK0005).
/// </summary>
/// <remarks>
///     One instance per compilation, created in the compilation-start action and captured by the actions of that
///     compilation only, never stored in an analyzer field (RS1008). Thread safety: the symbol and syntax actions add
///     concurrently; the compilation-end action runs after all of them and is the only reader.
/// </remarks>
internal sealed class PluginCompilationState
{
    private readonly bool? _entryPointIsGenerated;
    private readonly ConcurrentQueue<(string Name, Location Location, bool IsManualBootstrap)> _entryPointTypes = new();
    private readonly ConcurrentQueue<(string Name, Location Location)> _pluginClasses = new();
    private readonly ConcurrentQueue<(string Name, Location Location)> _reservedNamespaces = new();

    /// <param name="entryPointIsGenerated">
    ///     <see langword="true" /> when a direct package reference made generated bootstrap mode explicit;
    ///     <see langword="false" /> for explicit manual-bootstrap mode; <see langword="null" /> when no direct build
    ///     contract reached this compilation.
    /// </param>
    public PluginCompilationState(bool? entryPointIsGenerated)
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

    /// <summary>Records source code that declares the exact type identity the host reserves for its managed bootstrap.</summary>
    public void AddEntryPointType(string name, Location location, bool isManualBootstrap)
    {
        _entryPointTypes.Enqueue((name, location, isManualBootstrap));
    }

    /// <summary>The compilation-end action: reports CESDK0002 through CESDK0005.</summary>
    public void Report(CompilationAnalysisContext context)
    {
        var pluginClassCount = _pluginClasses.Count;
        if (pluginClassCount == 0)
            // Not a plugin assembly (the SDK's own libraries, a helper library): both rules are about plugins.
            return;

        if (pluginClassCount > 1 && _entryPointIsGenerated is true)
            foreach (var (name, location) in _pluginClasses)
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MultiplePluginClasses, location, name,
                    pluginClassCount));

        if (_entryPointIsGenerated is true)
        {
            foreach (var (name, location) in _reservedNamespaces)
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ReservedNamespace, location, name));

            foreach (var (name, location, _) in _entryPointTypes)
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GeneratedEntryPointCollision, location, name));

            return;
        }

        if (_entryPointIsGenerated is not false) return;

        var hasManualBootstrap = false;
        foreach (var (_, _, isManualBootstrap) in _entryPointTypes)
            if (isManualBootstrap)
            {
                hasManualBootstrap = true;
                break;
            }

        if (hasManualBootstrap) return;

        Location locationForManualBootstrap = Location.None;
        foreach (var (_, location, _) in _entryPointTypes)
        {
            locationForManualBootstrap = location;
            break;
        }

        var requirement = locationForManualBootstrap == Location.None
            ? "the assembly declares no static CESDK.CESDK type with public static int CEPluginInitialize(System.IntPtr, int)"
            : "CESDK.CESDK has no public static int CEPluginInitialize(System.IntPtr, int) method";
        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InvalidManualBootstrap, locationForManualBootstrap,
            requirement));
    }
}
