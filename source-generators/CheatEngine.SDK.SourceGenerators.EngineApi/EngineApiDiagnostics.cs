using CheatEngine.SDK.SourceGenerators.EngineApi.Model;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.EngineApi;

/// <summary>Descriptors and location construction for curated Engine API spec-file diagnostics.</summary>
internal static class EngineApiDiagnostics
{
	private const string Category = "CheatEngine.SDK.EngineApi";

	private static readonly DiagnosticDescriptor InvalidSpec = new(
		"CESDK3001",
		"Engine API specification is invalid",
		"Engine API specification: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"Correct the listed Engine API spec grammar error; invalid specs cannot remove generated API silently.");

	private static readonly DiagnosticDescriptor ConflictingSpec = new(
		"CESDK3002",
		"Engine API specification has a generated-identity conflict",
		"Engine API specification: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"One Engine API spec file owns each generated type and its members and cache fields.");

	/// <summary>Creates the compiler diagnostic for one Roslyn-free parsed issue.</summary>
	public static Diagnostic Create(SpecFileModel spec, SpecIssue issue)
	{
		int line = issue.Line > 0 ? issue.Line - 1 : 0;
		int column = issue.Column > 0 ? issue.Column - 1 : 0;
		LinePosition position = new(line, column);
		Location location = Location.Create(
			spec.SourcePath,
			new TextSpan(0, 0),
			new LinePositionSpan(position, position));

		return Diagnostic.Create(
			issue.Kind == SpecIssueKind.Conflict ? ConflictingSpec : InvalidSpec,
			location,
			issue.Message);
	}
}
