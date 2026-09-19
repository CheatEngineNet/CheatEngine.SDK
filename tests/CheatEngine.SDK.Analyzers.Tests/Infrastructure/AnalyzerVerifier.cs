using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     Entry points for analyzer tests. Expected diagnostics are written in the source with the testing library's
///     markup (<c>{|CESDK0001:span|}</c>), or as a <see cref="DiagnosticResult" /> bound to a <c>{|#0:span|}</c>
///     location when the message arguments matter. Anything unexpected, compiler errors included, fails the test.
/// </summary>
internal static class AnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <summary>An expected diagnostic of <paramref name="descriptor" />; add location and arguments fluently.</summary>
    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
    {
        return new DiagnosticResult(descriptor);
    }

    /// <summary>Verifies a single-file plugin project that references the contract stubs.</summary>
    public static Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        return VerifyAsync([("Test0.cs", source)], expected);
    }

    /// <summary>Verifies a multi-file plugin project that references the contract stubs.</summary>
    public static Task VerifyAsync((string FileName, string Source)[] sources, params DiagnosticResult[] expected)
    {
        return RunAsync(new CheatEngineSdkAnalyzerTest<TAnalyzer>(), sources, expected);
    }

    /// <summary>
    ///     Verifies a plugin project that exposes one MSBuild property to the compiler, the way
    ///     <c>CompilerVisibleProperty</c> does: as <c>build_property.&lt;name&gt;</c> in a global analyzer config.
    /// </summary>
    public static Task VerifyWithBuildPropertyAsync(string name, string value, string source,
        params DiagnosticResult[] expected)
    {
        CheatEngineSdkAnalyzerTest<TAnalyzer> test = new();
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig",
            TestText.Normalize($"is_global = true\nbuild_property.{name} = {value}\n")));
        return RunAsync(test, [("Test0.cs", source)], expected);
    }

    /// <summary>Verifies a project that does not reference CheatEngine.SDK at all: the analyzers must stay out of the way.</summary>
    public static Task VerifyWithoutCheatEngineSdkAsync(string source, params DiagnosticResult[] expected)
    {
        return RunAsync(new CheatEngineSdkAnalyzerTest<TAnalyzer>(false), [("Test0.cs", source)], expected);
    }

    private static Task RunAsync(CheatEngineSdkAnalyzerTest<TAnalyzer> test, (string FileName, string Source)[] sources,
        DiagnosticResult[] expected)
    {
        foreach (var (fileName, source) in sources) test.TestState.Sources.Add((fileName, TestText.Normalize(source)));

        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
