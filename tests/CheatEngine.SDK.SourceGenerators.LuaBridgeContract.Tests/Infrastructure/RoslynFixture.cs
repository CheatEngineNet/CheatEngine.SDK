using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

/// <summary>Direct in-memory Roslyn harness: the generator receives only compiler-provided additional text.</summary>
internal static class RoslynFixture
{
    private static readonly MetadataReference[] References =
        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14, DocumentationMode.Diagnose);

    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable,
        warningLevel: 9999);

    public static GeneratorRun Run(string path, string text)
    {
        var additionalText = new InMemoryAdditionalText(path, text);
        return GeneratorRun.Execute(CreateDriver(additionalText), CreateCompilation());
    }

    public static GeneratorRun Run(params (string Path, string Text)[] catalogs)
    {
        var additionalTexts = new AdditionalText[catalogs.Length];
        for (var i = 0; i < catalogs.Length; i++)
            additionalTexts[i] = new InMemoryAdditionalText(catalogs[i].Path, catalogs[i].Text);
        return GeneratorRun.Execute(CreateDriver(additionalTexts), CreateCompilation());
    }

    public static GeneratorDriver CreateDriver(params AdditionalText[] additionalTexts)
    {
        return CSharpGeneratorDriver.Create(
            [new LuaBridgeContractGenerator().AsSourceGenerator()],
            additionalTexts,
            ParseOptions,
            null,
            new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
    }

    public static CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create("LuaBridgeContractGeneratorTest", [], References, CompilationOptions);
    }
}
