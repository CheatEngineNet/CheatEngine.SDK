using System;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CESDK.SourceGenerators.Shared;

/// <summary>
///     Reads MSBuild properties that the build exposes to the compiler through <c>CompilerVisibleProperty</c>.
/// </summary>
/// <remarks>
///     Such a property reaches a generator as the global analyzer-config key <c>build_property.&lt;Name&gt;</c>. The
///     value travels through a generated <c>.editorconfig</c> file, which loses anything after a <c>;</c> or <c>#</c>:
///     only booleans and plain identifiers are safe, hence the narrow API. Read the value inside a <c>Select</c> over
///     <c>AnalyzerConfigOptionsProvider</c> and keep only the parsed result in the pipeline model: the options object
///     itself is not value-equatable.
/// </remarks>
internal static class BuildProperty
{
    /// <summary>Prefix of every MSBuild property key; concatenate with the property name into a constant.</summary>
    public const string KeyPrefix = "build_property.";

    /// <summary>
    ///     Reads a boolean property. Returns <paramref name="defaultValue" /> when the key is absent (the property is not
    ///     compiler-visible, for example in a project that references the generator without the package's props), empty,
    ///     or not a boolean. Parsing follows MSBuild usage: case-insensitive <c>true</c>/<c>false</c>, surrounding white
    ///     space ignored.
    /// </summary>
    /// <param name="globalOptions"><c>AnalyzerConfigOptionsProvider.GlobalOptions</c>.</param>
    /// <param name="key">Full key, <see cref="KeyPrefix" /> included.</param>
    /// <param name="defaultValue">Value used when the property carries no usable boolean.</param>
    public static bool ReadBoolean(AnalyzerConfigOptions globalOptions, string key, bool defaultValue)
    {
        if (globalOptions is null) throw new ArgumentNullException(nameof(globalOptions));

        return globalOptions.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value)
            ? value
            : defaultValue;
    }
}
