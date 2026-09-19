using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CESDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     A code-fix test with the same compilation setup as <see cref="CesdkAnalyzerTest{TAnalyzer}" />. One run checks
///     the diagnostics, the fix applied one diagnostic at a time, and Fix All in document, project and solution.
/// </summary>
internal sealed class CesdkCodeFixTest<TAnalyzer, TCodeFix> : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public CesdkCodeFixTest()
    {
        ReferenceAssemblies = LocalFrameworkReferences.WithoutPackages;
        TestState.AdditionalReferences.AddRange(LocalFrameworkReferences.References);
        ContractStubs.AddTo(TestState);
    }
}
