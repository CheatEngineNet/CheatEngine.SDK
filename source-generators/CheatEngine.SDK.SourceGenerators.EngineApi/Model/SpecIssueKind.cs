namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>The diagnostic family a <see cref="SpecIssue" /> belongs to.</summary>
internal enum SpecIssueKind
{
    /// <summary>The source text does not meet the curated spec grammar.</summary>
    Grammar,

    /// <summary>Two otherwise valid specs would generate the same C# identity.</summary>
    Conflict,
}
