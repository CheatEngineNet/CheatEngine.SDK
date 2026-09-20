namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>CE 7.7 facts shared by every entry in one <c>contract: ce77</c> spec file.</summary>
/// <param name="Provenance">The proof status and source reference.</param>
/// <param name="MinimumCheatEngineVersion">The minimum exact CE file version.</param>
/// <param name="Architecture">The supported host architecture.</param>
/// <param name="ThreadAffinity">The declared thread-affinity token.</param>
/// <param name="Ownership">The declared ownership token.</param>
internal sealed record SpecFileContract(
    string Provenance,
    string MinimumCheatEngineVersion,
    string Architecture,
    string ThreadAffinity,
    string Ownership);
