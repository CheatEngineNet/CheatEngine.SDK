namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>
///     One reason a spec file entry, or the whole file, produced no output. Never reported as a compiler diagnostic
///     (generators never report diagnostics): this is a dependency-free list a test asserts against, kept only for
///     the tests of this repository-internal generator.
/// </summary>
/// <param name="Line">1-based line number inside the spec file that the issue is about.</param>
/// <param name="Message">A human-readable explanation in English, not copied from Cheat Engine's documentation.</param>
internal sealed record SpecIssue(int Line, string Message);
