using System.Collections.Immutable;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;

/// <summary>The validated catalogue, if any, and every diagnostic recovered without throwing.</summary>
internal sealed record CatalogParseResult(
    string SourcePath,
    CatalogModel? Catalog,
    ImmutableArray<CatalogDiagnostic> Diagnostics);
