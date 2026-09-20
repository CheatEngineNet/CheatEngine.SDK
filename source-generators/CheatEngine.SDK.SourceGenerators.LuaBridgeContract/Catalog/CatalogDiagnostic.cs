using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;

/// <summary>A diagnostic whose source span is stored as plain values rather than a Roslyn syntax object.</summary>
internal sealed record CatalogDiagnostic(
    string Path,
    TextSpan Span,
    LinePositionSpan LineSpan,
    string Message,
    bool IsConflict);
