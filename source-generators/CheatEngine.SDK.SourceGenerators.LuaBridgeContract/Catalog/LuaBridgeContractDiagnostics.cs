using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;

/// <summary>Descriptors and external-file locations for the repository-only bridge catalogue generator.</summary>
internal static class LuaBridgeContractDiagnostics
{
	private const string Category = "CheatEngine.SDK.LuaBridge";

	private static readonly DiagnosticDescriptor InvalidCatalog = new(
		"CESDK4001",
		"Protected Lua operation catalogue is invalid",
		"Protected Lua operation catalogue: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"Correct the invalid protected-operations.json entry; the managed bridge contract is not generated from an invalid catalogue.");

	private static readonly DiagnosticDescriptor ConflictingCatalog = new(
		"CESDK4002",
		"Protected Lua operation catalogue is ambiguous",
		"Protected Lua operation catalogue: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"CheatEngine.SDK.Lua.Interop must supply exactly one protected-operations.json AdditionalFile.");

	public static CatalogDiagnostic Ambiguous(string path)
	{
		return new CatalogDiagnostic(
			path,
			new TextSpan(0, 0),
			new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)),
			"More than one protected-operations.json AdditionalFile was supplied; only one catalogue may own the generated managed operation contract.",
			true);
	}

	public static Diagnostic Create(CatalogDiagnostic diagnostic)
	{
		DiagnosticDescriptor descriptor = diagnostic.IsConflict ? ConflictingCatalog : InvalidCatalog;
		Location location = Location.Create(diagnostic.Path, diagnostic.Span, diagnostic.LineSpan);
		return Diagnostic.Create(descriptor, location, diagnostic.Message);
	}
}
