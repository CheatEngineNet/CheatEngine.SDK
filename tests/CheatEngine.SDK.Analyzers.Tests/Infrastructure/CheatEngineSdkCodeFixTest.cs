using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     A code-fix test with the same compilation setup as <see cref="CheatEngineSdkAnalyzerTest{TAnalyzer}" />. One run checks
///     the diagnostics, the fix applied one diagnostic at a time, and Fix All in document, project and solution.
/// </summary>
internal sealed class CheatEngineSdkCodeFixTest<TAnalyzer, TCodeFix> : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public CheatEngineSdkCodeFixTest()
    {
        ReferenceAssemblies = LocalFrameworkReferences.WithoutPackages;
        TestState.AdditionalReferences.AddRange(LocalFrameworkReferences.References);
        ContractStubs.AddTo(TestState);
        TestState.AnalyzerConfigFiles.Add(("/.globalconfig",
            TestText.Normalize("is_global = true\nbuild_property.CheatEngineSdkGenerateEntryPoint = true\n")));
    }
}
