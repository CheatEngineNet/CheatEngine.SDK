using System;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared;

/// <summary>
///     Naming convention for incremental-pipeline steps (<c>WithTrackingName</c>) shared by every CheatEngine.SDK
///     generator.
/// </summary>
/// <remarks>
///     <para>
///         Every step of every pipeline is named, with a constant of the form
///         <c>CheatEngine.SDK.&lt;Generator&gt;.&lt;Step&gt;</c>. Each generator declares its own constants class on top
///         of
///         <see cref="Prefix" /> (built with <see langword="const" /> concatenation, so the names stay usable in
///         attributes
///         and switch labels).
///     </para>
///     <para>
///         The common prefix is what makes the cacheability gate generic: a test enumerates
///         <c>GeneratorRunResult.TrackedSteps</c>, keeps the names for which <see cref="IsCheatEngineSdkStep" /> is true,
///         and
///         asserts
///         <c>Cached</c>/<c>Unchanged</c> on all of them. A step added later is covered without touching the test, and
///         Roslyn's own unnamed or internally named steps are left out.
///     </para>
/// </remarks>
[SuppressMessage(
    "Meziantou.Analyzer",
    "MA0182",
    Justification =
        "This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class TrackingNames
{
    /// <summary>Prefix of every CheatEngine.SDK step name.</summary>
    public const string Prefix = "CheatEngine.SDK.";

    /// <summary><see langword="true" /> when <paramref name="stepName" /> follows the CheatEngine.SDK convention.</summary>
    public static bool IsCheatEngineSdkStep(string? stepName)
    {
        return stepName is not null && stepName.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
