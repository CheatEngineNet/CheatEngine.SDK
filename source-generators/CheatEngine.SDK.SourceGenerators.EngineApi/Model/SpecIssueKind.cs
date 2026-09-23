namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>The diagnostic family a <see cref="SpecIssue" /> belongs to; each family is one CESDK3xxx identifier.</summary>
internal enum SpecIssueKind
{
	/// <summary>The source text does not meet the curated spec grammar (CESDK3001).</summary>
	Grammar,

	/// <summary>Two otherwise valid specs would generate the same C# identity (CESDK3002).</summary>
	Conflict,

	/// <summary>A spec file with entries does not declare the <c>contract: ce77</c> evidence header (CESDK3003).</summary>
	MissingContract,

	/// <summary>An <c>opt:</c> argument is followed by a required one, or has a kind that cannot be optional (CESDK3004).</summary>
	OptionalArgument,

	/// <summary>
	///     An <c>opt-result:</c> or <c>rest:</c> result is out of order, on the wrong form, or of a kind it cannot have
	///     (CESDK3005).
	/// </summary>
	ResultShape
}
