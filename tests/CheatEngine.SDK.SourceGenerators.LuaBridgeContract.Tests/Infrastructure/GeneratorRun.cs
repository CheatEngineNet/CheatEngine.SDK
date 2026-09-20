using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

/// <summary>One deterministic run of the bridge-contract generator.</summary>
internal sealed class GeneratorRun
{
    private GeneratorRun(GeneratorDriver driver, Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics,
        GeneratorRunResult result)
    {
        Driver = driver;
        OutputCompilation = outputCompilation;
        GeneratorDiagnostics = diagnostics;
        Result = result;
    }

    public GeneratorDriver Driver { get; }

    public Compilation OutputCompilation { get; }

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

    public GeneratorRunResult Result { get; }

    public ImmutableArray<GeneratedSourceResult> GeneratedSources => Result.GeneratedSources;

    public string SingleGeneratedText => Assert.Single(GeneratedSources).SourceText.ToString();

    public static GeneratorRun Execute(GeneratorDriver driver, Compilation compilation)
    {
        var updated = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics,
            TestContext.Current.CancellationToken);
        var driverResult = updated.GetRunResult();
        Assert.Single(driverResult.Results);
        return new GeneratorRun(updated, outputCompilation, diagnostics, driverResult.Results[0]);
    }

    public void AssertCompilesClean()
    {
        Assert.Null(Result.Exception);
        Assert.Empty(GeneratorDiagnostics);
        var diagnostics = OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken);
        for (var i = 0; i < diagnostics.Length; i++)
            Assert.True(diagnostics[i].Severity < DiagnosticSeverity.Warning, diagnostics[i].ToString());
    }
}
