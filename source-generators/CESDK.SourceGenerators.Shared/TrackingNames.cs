using System;

namespace CESDK.SourceGenerators.Shared;

/// <summary>
///     Naming convention for incremental-pipeline steps (<c>WithTrackingName</c>) shared by every CESDK generator.
/// </summary>
/// <remarks>
///     <para>
///         Every step of every pipeline is named, with a constant of the form
///         <c>CESDK.&lt;Generator&gt;.&lt;Step&gt;</c>. Each generator declares its own constants class on top of
///         <see cref="Prefix" /> (built with <see langword="const" /> concatenation, so the names stay usable in
///         attributes
///         and switch labels).
///     </para>
///     <para>
///         The common prefix is what makes the cacheability gate generic: a test enumerates
///         <c>GeneratorRunResult.TrackedSteps</c>, keeps the names for which <see cref="IsCesdkStep" /> is true, and
///         asserts
///         <c>Cached</c>/<c>Unchanged</c> on all of them. A step added later is covered without touching the test, and
///         Roslyn's own unnamed or internally named steps are left out.
///     </para>
/// </remarks>
internal static class TrackingNames
{
    /// <summary>Prefix of every CESDK step name.</summary>
    public const string Prefix = "CESDK.";

    /// <summary><see langword="true" /> when <paramref name="stepName" /> follows the CESDK convention.</summary>
    public static bool IsCesdkStep(string? stepName)
    {
        return stepName is not null && stepName.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
