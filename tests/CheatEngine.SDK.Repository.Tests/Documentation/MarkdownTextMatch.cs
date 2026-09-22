namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>A piece of raw text and the 1-based line where it starts.</summary>
internal readonly record struct MarkdownTextMatch(int Line, string Value);
