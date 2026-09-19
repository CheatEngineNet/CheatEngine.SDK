namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>Text helpers shared by every test.</summary>
internal static class TestText
{
    /// <summary>
    ///     Rewrites every line break to <see cref="Environment.NewLine" />. The raw string literals of the tests carry
    ///     whatever line ending the test file was checked out with, while the Roslyn formatter writes the platform
    ///     line ending for the lines a code fix adds: without this the fixed text would depend on git settings.
    /// </summary>
    public static string Normalize(string source)
    {
        return source.ReplaceLineEndings();
    }

    /// <summary>
    ///     Indents every non-empty line by four spaces per level, so that a multi-line member can be spliced into a
    ///     class template and still compare equal, character for character, with formatted code-fix output.
    /// </summary>
    public static string Indent(string text, int levels = 1)
    {
        string indentation = new(' ', 4 * levels);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Length > 0)
                lines[index] = indentation + lines[index];

        return string.Join('\n', lines);
    }
}
