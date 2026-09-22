namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>The result of looking a repository-relative path up segment by segment with ordinal comparison.</summary>
internal readonly record struct PathLookup(bool Exists, bool IsDirectory, string? Problem);
