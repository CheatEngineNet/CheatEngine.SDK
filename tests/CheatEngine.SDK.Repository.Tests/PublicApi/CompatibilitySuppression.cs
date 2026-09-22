namespace CheatEngine.SDK.Repository.Tests.PublicApi;

/// <summary>One <c>&lt;Suppression&gt;</c> of <c>src/CheatEngine.SDK/CompatibilitySuppressions.xml</c>.</summary>
internal sealed record CompatibilitySuppression(
	string DiagnosticId,
	string Target,
	string Left,
	string Right,
	bool IsBaselineSuppression)
{
	/// <summary>The line this suppression contributes to <c>eng/api/client-induced-breaks.txt</c>.</summary>
	public string InducedBreakLine => $"{Target} | {DiagnosticId}";
}
