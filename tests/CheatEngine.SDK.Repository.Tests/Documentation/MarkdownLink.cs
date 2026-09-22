namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>A link target as written (backslash escapes removed, still percent-encoded) and the 1-based line of its destination.</summary>
internal readonly record struct MarkdownLink(MarkdownLinkKind Kind, string Target, int Line);
