using System.Diagnostics.CodeAnalysis;

// SonarAnalyzer rule S6640 flags every unsafe context. These tests drive the unsafe surface of the SDK directly,
// so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Test code drives the unsafe interop surface.")]
