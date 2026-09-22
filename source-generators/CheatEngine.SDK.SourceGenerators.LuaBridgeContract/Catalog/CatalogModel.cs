using System.Collections.Immutable;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;

/// <summary>The generator-relevant, value-only projection of the protected operation catalogue.</summary>
internal sealed record CatalogModel(
	string SourcePath,
	ImmutableArray<CatalogOperation> Operations,
	ulong RequiredBitmap);
