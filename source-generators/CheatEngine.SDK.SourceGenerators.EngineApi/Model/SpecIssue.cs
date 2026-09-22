namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>
///     One problem in a spec file. The model deliberately contains only value types, so the incremental pipeline never
///     captures Roslyn objects; <c>EngineApiGenerator</c> turns every issue into a compiler diagnostic at this location.
/// </summary>
/// <param name="Line">1-based line number inside the spec file that the issue is about.</param>
/// <param name="Message">A human-readable explanation in English, not copied from Cheat Engine's documentation.</param>
/// <param name="Column">1-based column number inside the spec file that the issue is about.</param>
/// <param name="Kind">Whether this is malformed input or a conflict introduced while combining spec files.</param>
internal sealed record SpecIssue(
	int Line,
	string Message,
	int Column = 1,
	SpecIssueKind Kind = SpecIssueKind.Grammar);
