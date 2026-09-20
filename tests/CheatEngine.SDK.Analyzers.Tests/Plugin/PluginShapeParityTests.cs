using System.Collections.Immutable;
using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Plugin;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using CheatEngine.SDK.SourceGenerators.EntryPoint;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Tests.Plugin;

/// <summary>
///     The plugin-shape predicate that both the entry-point generator and CESDK0001 call
///     (<c>source-generators/CheatEngine.SDK.SourceGenerators.Shared/Shapes/PluginShape.cs</c>) is exercised here through
///     its two
///     real call sites -
///     <c>CheatEngine.SDK.SourceGenerators.EntryPoint.EntryPointGenerator</c> and <c>CheatEnginePluginAnalyzer</c> - run
///     over the
///     exact same compilation, and the two verdicts are asserted to agree for every shape of the matrix: the generator
///     emits an entry point for a plugin class if and only if CESDK0001 says nothing about it.
/// </summary>
/// <remarks>
///     Lives here, not in a separate shared test project: this project already has the
///     analyzer under test as a normal reference, the framework-agnostic Roslyn testing packages, and the local
///     framework-reference resolution (<see cref="LocalFrameworkReferences" />) that avoids a network restore; the
///     entry-point generator project is the one additional normal reference this parity matrix needs (its own tests
///     do the equivalent for the analyzer's side: <c>DefaultVerifierTests</c> runs the generator under
///     <c>CSharpSourceGeneratorTest&lt;,DefaultVerifier&gt;</c>). The shape matrix below copies the exact source
///     snippets of <c>tests/CheatEngine.SDK.SourceGenerators.EntryPoint.Tests/Generator/NoOutputTests.InvalidShapes</c>
///     and
///     <c>ValidShapeTests.ValidShapes</c> (copied, not referenced: test projects do not reference each other's test
///     code), plus these further shapes: required members without <c>[SetsRequiredMembers]</c>,
///     <c>[Obsolete(error: true)]</c>, a <see langword="file" /> class, optional/params-only constructors,
///     <see langword="internal" />/<see langword="protected internal" /> constructors, a generic container, a record, and
///     a primary constructor.
/// </remarks>
public sealed class PluginShapeParityTests
{
    private const string Usings = "using CheatEngine.SDK.Annotations.Plugin; using CheatEngine.SDK.Hosting.Plugin;\n";

    private const string Body = "{ protected override void OnEnable() { } protected override void OnDisable() { } }";

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

    public static TheoryData<string, string, bool> Shapes
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (var (shape, declaration, expectedValid) in ClassAndConstructorValidShapes())
                data.Add(shape, declaration, expectedValid);
            foreach (var (shape, declaration, expectedValid) in AdvancedValidShapes())
                data.Add(shape, declaration, expectedValid);
            foreach (var (shape, declaration, expectedValid) in ClassShapeRejections())
                data.Add(shape, declaration, expectedValid);
            foreach (var (shape, declaration, expectedValid) in AccessibilityAndBaseRejections())
                data.Add(shape, declaration, expectedValid);
            foreach (var (shape, declaration, expectedValid) in ConstructorRejections())
                data.Add(shape, declaration, expectedValid);
            foreach (var (shape, declaration, expectedValid) in NameAndEntryPointRejections())
                data.Add(shape, declaration, expectedValid);
            return data;
        }
    }

    // Mirrors ValidShapeTests.ValidShapes, plus the required-members and record/primary-constructor cases.
    private static IEnumerable<(string Shape, string Declaration, bool ExpectedValid)> ClassAndConstructorValidShapes()
    {
        yield return ("sealed class", $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body}",
            true);
        yield return ("internal class",
            $"[CheatEnginePlugin(\"P\")] internal sealed class P : CheatEnginePlugin {Body}", true);
        yield return ("unsealed class", $"[CheatEnginePlugin(\"P\")] public class P : CheatEnginePlugin {Body}", true);
        yield return (
            "internal constructor",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ internal P() {{ }} {Body[1..]}",
            true);
        yield return (
            "protected internal constructor",
            $"[CheatEnginePlugin(\"P\")] public class P : CheatEnginePlugin {{ protected internal P() {{ }} {Body[1..]}",
            true);
        yield return (
            "extra constructors",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P() {{ }} public P(int value) {{ _ = value; }} {Body[1..]}",
            true);
        yield return (
            "indirect derivation",
            $"public abstract class Base : CheatEnginePlugin {Body} [CheatEnginePlugin(\"P\")] public sealed class P : Base {{ }}",
            true);
    }

    private static IEnumerable<(string Shape, string Declaration, bool ExpectedValid)> AdvancedValidShapes()
    {
        yield return (
            "primary constructor without parameters",
            $"[CheatEnginePlugin(\"P\")] public sealed class P() : CheatEnginePlugin {Body}", true);
        yield return (
            "obsolete as a warning",
            $"[CheatEnginePlugin(\"P\")] [System.Obsolete(\"Use the new plugin.\")] public sealed class P : CheatEnginePlugin {Body}",
            true);
        yield return (
            "required members set by the constructor",
            """
            [CheatEnginePlugin("P")]
            public sealed class P : CheatEnginePlugin
            {
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                public P() => Value = 1;

                public required int Value { get; init; }

                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """,
            true);
    }

    // Invalid shapes (mirrors NoOutputTests.InvalidShapes, plus the predicate's RequiredMembers and ObsoleteError
    // checks), split by category so every helper stays comfortably under MA0051's line limit.
    private static IEnumerable<(string Shape, string Declaration, bool ExpectedValid)> ClassShapeRejections()
    {
        yield return ("abstract class",
            $"[CheatEnginePlugin(\"P\")] public abstract class P : CheatEnginePlugin {Body}", false);
        yield return ("static class", "[CheatEnginePlugin(\"P\")] public static class P { }", false);
        yield return ("generic class",
            $"[CheatEnginePlugin(\"P\")] public sealed class P<T> : CheatEnginePlugin {Body}", false);
        yield return (
            "nested in a generic class",
            $"public static class Outer<T> {{ [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body} }}",
            false);
    }

    private static IEnumerable<(string Shape, string Declaration, bool ExpectedValid)> AccessibilityAndBaseRejections()
    {
        yield return ("not derived from the plugin base", "[CheatEnginePlugin(\"P\")] public sealed class P { }",
            false);
        yield return (
            "derived from a look-alike base",
            "namespace Other.Hosting { public abstract class CheatEnginePlugin { } } [CheatEnginePlugin(\"P\")] public sealed class P : Other.Hosting.CheatEnginePlugin { }",
            false);
        yield return (
            "private nested class",
            $"public static class Outer {{ [CheatEnginePlugin(\"P\")] private sealed class P : CheatEnginePlugin {Body} }}",
            false);
        yield return (
            "protected nested class",
            $"public class Outer {{ [CheatEnginePlugin(\"P\")] protected sealed class P : CheatEnginePlugin {Body} }}",
            false);
        yield return (
            "nested in a private class",
            $"public static class Outer {{ private static class Hidden {{ [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body} }} }}",
            false);
        yield return ("file-local class", $"[CheatEnginePlugin(\"P\")] file sealed class P : CheatEnginePlugin {Body}",
            false);
    }

    private static IEnumerable<(string Shape, string Declaration, bool ExpectedValid)> ConstructorRejections()
    {
        yield return (
            "only optional parameters",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P(int value = 0) {{ _ = value; }} {Body[1..]}",
            false);
        yield return (
            "trailing params constructor",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P(params int[] xs) {{ _ = xs; }} {Body[1..]}",
            false);
        yield return (
            "no parameterless constructor",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public P(int value) {{ _ = value; }} {Body[1..]}",
            false);
        yield return (
            "private constructor",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ private P() {{ }} {Body[1..]}",
            false);
        yield return (
            "protected constructor",
            $"[CheatEnginePlugin(\"P\")] public class P : CheatEnginePlugin {{ protected P() {{ }} {Body[1..]}", false);
        yield return (
            "required member without a constructor that sets it",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ public required int Value {{ get; init; }} {Body[1..]}",
            false);
        yield return (
            "obsolete as error on the class",
            $"[System.Obsolete(\"no\", true)] [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body}",
            false);
        yield return (
            "obsolete as error on the constructor",
            $"[CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {{ [System.Obsolete(\"no\", true)] public P() {{ }} {Body[1..]}",
            false);
    }

    private static IEnumerable<(string Shape, string Declaration, bool ExpectedValid)> NameAndEntryPointRejections()
    {
        yield return ("empty name", $"[CheatEnginePlugin(\"\")] public sealed class P : CheatEnginePlugin {Body}",
            false);
        yield return (
            "white-space name",
            $"[CheatEnginePlugin(\" \\t\\u00A0\")] public sealed class P : CheatEnginePlugin {Body}", false);
        yield return ("null name", $"[CheatEnginePlugin(null!)] public sealed class P : CheatEnginePlugin {Body}",
            false);
        yield return ("missing name argument", $"[CheatEnginePlugin] public sealed class P : CheatEnginePlugin {Body}",
            false);

        // Not "struct": the compiler itself rejects the attribute there (CS0592, AttributeTargets.Class), left
        // alone on purpose by both sides (see analyzers/docs/CESDK0001.md) - not part of the shared
        // predicate's contract, so not part of this parity matrix either.
        yield return ("record class", "[CheatEnginePlugin(\"P\")] public sealed record P;", false);
        yield return (
            "named like the entry point",
            $"namespace CESDK {{ [CheatEnginePlugin(\"P\")] public sealed class CESDK : CheatEnginePlugin {Body} }}",
            false);
        yield return (
            "nested in a type named like the entry point",
            $"namespace CESDK {{ public static class CESDK {{ [CheatEnginePlugin(\"P\")] public sealed class P : CheatEnginePlugin {Body} }} }}",
            false);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task Generator_and_analyzer_agree_on_every_shape(string shape, string declaration, bool expectedValid)
    {
        var compilation = CreateCompilation(Usings + declaration);

        var generatorEmits = RunGenerator(compilation);
        var analyzerReportsInvalidPluginClass = await AnalyzerReportsInvalidPluginClassAsync(compilation);

        Assert.True(
            generatorEmits == expectedValid,
            $"'{shape}': the generator {(generatorEmits ? "emitted" : "stayed silent")}, expected {(expectedValid ? "an entry point" : "silence")}.");
        Assert.True(
            analyzerReportsInvalidPluginClass != expectedValid,
            $"'{shape}': CESDK0001 {(analyzerReportsInvalidPluginClass ? "reported" : "stayed silent")}, expected it to {(expectedValid ? "stay silent" : "report")}.");
    }

    private static CSharpCompilation CreateCompilation(string pluginSource)
    {
        return CSharpCompilation.Create(
            "PluginShapeParityAssembly",
            [
                CSharpSyntaxTree.ParseText(TestText.Normalize(pluginSource), ParseOptions, "Plugin.cs")
            ],
            LocalFrameworkReferences.References.AddRange(ContractStubs.References),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    // Same driver shape as CheatEngine.SDK.SourceGenerators.EntryPoint.Tests' GeneratorRun/RoslynFixture: "emits" means at
    // least one generated source, which for this generator only ever happens for exactly one valid plugin class.
    private static bool RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new EntryPointGenerator().AsSourceGenerator()],
            [],
            ParseOptions,
            DirectPackageAnalyzerConfigOptions.Enabled,
            new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
            TestContext.Current.CancellationToken);
        return !driver.GetRunResult().Results.Single().GeneratedSources.IsEmpty;
    }

    private static async Task<bool> AnalyzerReportsInvalidPluginClassAsync(CSharpCompilation compilation)
    {
        AnalyzerOptions options = new(ImmutableArray<AdditionalText>.Empty, DirectPackageAnalyzerConfigOptions.Enabled);
        var withAnalyzers = compilation.WithAnalyzers([new CheatEnginePluginAnalyzer()], options);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
        return diagnostics.Any(static diagnostic =>
            string.Equals(diagnostic.Id, DiagnosticIds.InvalidPluginClass, StringComparison.Ordinal));
    }
}
