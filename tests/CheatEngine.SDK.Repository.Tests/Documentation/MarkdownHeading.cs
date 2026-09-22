namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>An ATX heading, its text as written and its GitHub anchor (duplicates already suffixed).</summary>
internal readonly record struct MarkdownHeading(int Level, string Text, string Anchor, int Line);
