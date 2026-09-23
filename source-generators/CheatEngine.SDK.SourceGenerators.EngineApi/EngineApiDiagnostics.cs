using CheatEngine.SDK.SourceGenerators.EngineApi.Model;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.EngineApi;

/// <summary>Descriptors and location construction for curated Engine API spec-file diagnostics.</summary>
internal static class EngineApiDiagnostics
{
	private const string Category = "CheatEngine.SDK.EngineApi";

	// One markdown page per rule, named after the identifier, like the CheatEngine.SDK.Analyzers rules.
	private const string HelpLinkBase = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/";

	/// <summary>CESDK3001: a malformed header, entry, key, name or kind.</summary>
	internal static readonly DiagnosticDescriptor InvalidSpec = new(
		"CESDK3001",
		"Engine API specification is invalid",
		"Engine API specification: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"Correct the listed Engine API spec grammar error; invalid specs cannot remove generated API silently.",
		HelpLinkBase + "CESDK3001.md");

	/// <summary>CESDK3002: two spec files would generate the same type, member or cache field.</summary>
	internal static readonly DiagnosticDescriptor ConflictingSpec = new(
		"CESDK3002",
		"Engine API specification has a generated-identity conflict",
		"Engine API specification: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"One Engine API spec file owns each generated type and its members and cache fields.",
		HelpLinkBase + "CESDK3002.md");

	/// <summary>CESDK3003: a spec file with entries has no <c>contract: ce77</c> header.</summary>
	internal static readonly DiagnosticDescriptor MissingContract = new(
		"CESDK3003",
		"Engine API specification does not declare the ce77 contract",
		"Engine API specification: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"Every Engine API spec file with entries declares 'contract: ce77' with its provenance, minimum version, architecture, "
		+ "thread and ownership facts, and a 'nil' contract on every entry. A file without it generates nothing.",
		HelpLinkBase + "CESDK3003.md");

	/// <summary>CESDK3004: an optional argument is not trailing, or has a kind that cannot be optional.</summary>
	internal static readonly DiagnosticDescriptor InvalidOptionalArgument = new(
		"CESDK3004",
		"Engine API optional argument is invalid",
		"Engine API specification: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"An 'opt:' argument becomes a LuaOptional<T> parameter that can be omitted, so no 'arg:' or 'fixed:' argument may follow "
		+ "it, and its kind is one that LuaOptional<T> supports (not 'utf8' or 'string?'). The entry generates nothing.",
		HelpLinkBase + "CESDK3004.md");

	/// <summary>CESDK3005: an optional or variadic result is out of order, on the wrong form, or of an unsupported kind.</summary>
	internal static readonly DiagnosticDescriptor InvalidResultShape = new(
		"CESDK3005",
		"Engine API optional or variadic result is invalid",
		"Engine API specification: {0}",
		Category,
		DiagnosticSeverity.Error,
		true,
		"Results are read in order: 'result:' values, then 'opt-result:' values, then at most one 'rest:' tail, which only the "
		+ "'outcome' form can declare. An 'opt-result:' kind is one LuaOptional<T> supports; a 'rest:' kind is int32, int64, "
		+ "single, double or boolean. The entry generates nothing.",
		HelpLinkBase + "CESDK3005.md");

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

		return Diagnostic.Create(DescriptorFor(issue.Kind), location, issue.Message);
	}

	/// <summary>The descriptor of one issue family.</summary>
	internal static DiagnosticDescriptor DescriptorFor(SpecIssueKind kind)
	{
		return kind switch
		{
			SpecIssueKind.Conflict => ConflictingSpec,
			SpecIssueKind.MissingContract => MissingContract,
			SpecIssueKind.OptionalArgument => InvalidOptionalArgument,
			SpecIssueKind.ResultShape => InvalidResultShape,
			_ => InvalidSpec
		};
	}
}
