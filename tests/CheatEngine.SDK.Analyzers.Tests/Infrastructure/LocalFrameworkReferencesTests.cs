using CheatEngine.SDK.Analyzers.CodeFixes.Usage;
using CheatEngine.SDK.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     The test compilations must not need the network: no test asks the testing library to restore a
///     reference-assembly package, and the framework references are files of the local .NET installation.
/// </summary>
public sealed class LocalFrameworkReferencesTests
{
    [Fact]
    public void Analyzer_test_names_no_package_to_restore()
    {
        CheatEngineSdkAnalyzerTest<UnmanagedCallersOnlyGuardAnalyzer> test = new();

        AssertNothingToRestore(test.ReferenceAssemblies);
        Assert.NotEmpty(test.TestState.AdditionalReferences);
        Assert.Contains(test.TestState.AdditionalReferences,
            static reference => reference is PortableExecutableReference);
    }

    [Fact]
    public void Code_fix_test_names_no_package_to_restore()
    {
        CheatEngineSdkCodeFixTest<UnmanagedCallersOnlyGuardAnalyzer, UnmanagedCallersOnlyGuardCodeFixProvider> test =
            new();

        AssertNothingToRestore(test.ReferenceAssemblies);
        Assert.NotEmpty(test.TestState.AdditionalReferences);
        Assert.Contains(test.TestState.AdditionalReferences,
            static reference => reference is PortableExecutableReference);
    }

    [Fact]
    public void References_are_existing_local_files_and_include_the_core_facade()
    {
        var hasSystemRuntime = false;
        foreach (var reference in LocalFrameworkReferences.References)
        {
            var path = Assert.IsType<PortableExecutableReference>(reference, false).FilePath
                       ?? throw new InvalidOperationException("A framework reference without a file path.");
            Assert.True(File.Exists(path), path);
            hasSystemRuntime |= string.Equals(Path.GetFileName(path), "System.Runtime.dll",
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(hasSystemRuntime, "System.Runtime.dll is not among the framework references.");
    }

    [Fact]
    public void Running_runtime_fallback_yields_only_shared_framework_assemblies()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        Assert.NotEmpty(LocalFrameworkReferences.FromRunningRuntime());
        foreach (var reference in LocalFrameworkReferences.FromRunningRuntime())
        {
            var path = Assert.IsType<PortableExecutableReference>(reference, false).FilePath;
            Assert.Equal(runtimeDirectory, Path.GetDirectoryName(path), true);
        }
    }

    private static void AssertNothingToRestore(ReferenceAssemblies referenceAssemblies)
    {
        Assert.Null(referenceAssemblies.ReferenceAssemblyPackage);
        Assert.Empty(referenceAssemblies.Packages);
        Assert.Empty(referenceAssemblies.Assemblies);
    }
}
