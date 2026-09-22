using System.Collections.Immutable;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;

/// <summary>Combines the compiler inputs into the one deterministic generation decision.</summary>
internal sealed record LuaBridgeContractGenerationPlan(
	CatalogModel? Catalog,
	ImmutableArray<CatalogDiagnostic> Diagnostics)
{
	public static LuaBridgeContractGenerationPlan Create(ImmutableArray<CatalogParseResult> results)
	{
		if (results.IsDefaultOrEmpty)
		{
			return new LuaBridgeContractGenerationPlan(null, ImmutableArray<CatalogDiagnostic>.Empty);
		}

		if (results.Length == 1)
		{
			return new LuaBridgeContractGenerationPlan(results[0].Catalog, results[0].Diagnostics);
		}

		ImmutableArray<CatalogDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<CatalogDiagnostic>();
		for (int i = 0; i < results.Length; i++)
		{
			CatalogParseResult result = results[i];
			diagnostics.AddRange(result.Diagnostics);
			diagnostics.Add(LuaBridgeContractDiagnostics.Ambiguous(result.SourcePath));
		}

		return new LuaBridgeContractGenerationPlan(null, diagnostics.ToImmutable());
	}
}
