using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using CheatEngine.SDK.Analyzers.WellKnown;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.Analyzers.Tests.WellKnown;

/// <summary>
///     Resolving a contract must select its named SDK reference before it asks for the metadata name. Merged
///     compilation lookup either selects a source declaration or fails on duplicate referenced definitions.
/// </summary>
public sealed class SdkSymbolResolverTests
{
    private const string RequiresPluginEnabledAttribute =
        "CheatEngine.SDK.Annotations.Lifetime.RequiresPluginEnabledAttribute";

    private const string LookalikeAttributeSource = """
                                                    namespace CheatEngine.SDK.Annotations.Lifetime
                                                    {
                                                        public sealed class RequiresPluginEnabledAttribute : global::System.Attribute
                                                        {
                                                        }
                                                    }
                                                    """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable);

    [Fact]
    public void Annotation_source_lookalike_does_not_hide_the_sdk_contract()
    {
        var compilation = CreateCompilation(LookalikeAttributeSource);

        var resolved = SdkSymbolResolver.Annotation(compilation, RequiresPluginEnabledAttribute);

        Assert.NotNull(resolved);
        Assert.Equal("CheatEngine.SDK.Annotations", resolved!.ContainingAssembly.Identity.Name);
    }

    [Fact]
    public void Annotation_duplicate_referenced_lookalike_does_not_hide_the_sdk_contract()
    {
        var foreignLookalike = CreateReference("Foreign.Annotations", LookalikeAttributeSource);
        var compilation = CreateCompilation(string.Empty, foreignLookalike);

        var resolved = SdkSymbolResolver.Annotation(compilation, RequiresPluginEnabledAttribute);

        Assert.NotNull(resolved);
        Assert.Equal("CheatEngine.SDK.Annotations", resolved!.ContainingAssembly.Identity.Name);
    }

    private static CSharpCompilation CreateCompilation(string source, params MetadataReference[] additionalReferences)
    {
        var references = LocalFrameworkReferences.References.AddRange(ContractStubs.References);
        foreach (var reference in additionalReferences)
            references = references.Add(reference);

        return CSharpCompilation.Create(
            "SdkSymbolResolverTestAssembly",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Test.cs",
                cancellationToken: TestContext.Current.CancellationToken)],
            references,
            CompilationOptions);
    }

    private static PortableExecutableReference CreateReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, assemblyName + ".cs",
                cancellationToken: TestContext.Current.CancellationToken)],
            LocalFrameworkReferences.References,
            CompilationOptions);
        using MemoryStream image = new();
        var result = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, "The foreign lookalike did not compile:\n" + string.Join('\n', result.Diagnostics));

        return MetadataReference.CreateFromImage([.. image.ToArray()], filePath: assemblyName + ".dll");
    }
}
