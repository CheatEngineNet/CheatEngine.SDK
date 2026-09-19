using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     An analyzer test compiled against the locally installed .NET 10 reference assemblies
///     (<see cref="LocalFrameworkReferences" />: nothing is restored at test time) and, unless told otherwise, against the
///     <see cref="ContractStubs" /> project. Framework-agnostic: failures surface through <see cref="DefaultVerifier" />
///     as exceptions, which xUnit v3 reports like any other.
/// </summary>
internal sealed class CheatEngineSdkAnalyzerTest<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public CheatEngineSdkAnalyzerTest(bool referenceCheatEngineSdk = true)
    {
        ReferenceAssemblies = LocalFrameworkReferences.WithoutPackages;
        TestState.AdditionalReferences.AddRange(LocalFrameworkReferences.References);
        if (referenceCheatEngineSdk) ContractStubs.AddTo(TestState);
    }
}
