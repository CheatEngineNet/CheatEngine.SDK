namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>
///     Machine-validated CE 7.7 contract data attached to one generated wrapper. The curated spec keeps the native
///     evidence separate from the generated C# syntax, while still making the facts available to XML documentation,
///     diagnostics and later capability projection.
/// </summary>
/// <param name="Provenance">The evidence-status label and immutable source reference.</param>
/// <param name="MinimumCheatEngineVersion">The minimum four-part CE version, for example <c>7.7.0.10621</c>.</param>
/// <param name="Architecture">The curated host architecture, currently <c>x64</c>.</param>
/// <param name="ThreadAffinity">The verified or explicitly unknown thread-affinity token.</param>
/// <param name="Ownership">The ownership token for the wrapper's CE/Lua result and parameters.</param>
/// <param name="NilSemantics">How the CE Lua global represents absence or expected failure.</param>
internal sealed record SpecContract(
    string Provenance,
    string MinimumCheatEngineVersion,
    string Architecture,
    string ThreadAffinity,
    string Ownership,
    string NilSemantics);
