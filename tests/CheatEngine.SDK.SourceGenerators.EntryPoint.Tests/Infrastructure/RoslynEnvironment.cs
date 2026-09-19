using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>
///     What every test compilation needs and what is expensive to build: the <c>net10.0</c> framework references and
///     the compiled contract stubs. Built once per test process, from the local disk only (see
///     <see cref="LocalFrameworkReferences" />).
/// </summary>
internal sealed class RoslynEnvironment
{
    // Documentation comments are parsed and diagnosed, like in a project with GenerateDocumentationFile: the XML
    // comments the generator writes are then checked by the compiler too.
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14, DocumentationMode.Diagnose);

    // Strict on purpose: nullable on, unsafe OFF (the entry point must compile without it), every warning wave.
    public static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable,
        allowUnsafe: false,
        warningLevel: 9999);

    private static readonly Lazy<RoslynEnvironment> LazyShared =
        new(static () => Create(LocalFrameworkReferences.Load()));

    private RoslynEnvironment(ImmutableArray<MetadataReference> frameworkReferences, ImmutableArray<byte> stubsImage)
    {
        FrameworkReferences = frameworkReferences;
        StubsImage = stubsImage;
        StubsReference = MetadataReference.CreateFromImage(stubsImage, filePath: ContractStubs.AssemblyName + ".dll");
    }

    /// <summary>The process-wide environment over <see cref="LocalFrameworkReferences.Load" />.</summary>
    public static RoslynEnvironment Shared => LazyShared.Value;

    /// <summary><c>Microsoft.NETCore.App</c> 10.0: reference assemblies, or the running runtime as a fallback.</summary>
    public ImmutableArray<MetadataReference> FrameworkReferences { get; }

    /// <summary>The compiled <see cref="ContractStubs" />, to load for execution tests.</summary>
    public ImmutableArray<byte> StubsImage { get; }

    /// <summary>The compiled <see cref="ContractStubs" />, as a compilation reference.</summary>
    public MetadataReference StubsReference { get; }

    /// <summary>Framework + stubs: the references of a plugin compilation.</summary>
    public ImmutableArray<MetadataReference> PluginReferences => FrameworkReferences.Add(StubsReference);

    /// <summary>Compiles the contract stubs against <paramref name="frameworkReferences" />.</summary>
    /// <exception cref="InvalidOperationException">The stubs do not compile against these references.</exception>
    public static RoslynEnvironment Create(ImmutableArray<MetadataReference> frameworkReferences)
    {
        var stubs = CSharpCompilation.Create(
            ContractStubs.AssemblyName,
            [CSharpSyntaxTree.ParseText(ContractStubs.Source, ParseOptions, "ContractStubs.cs")],
            frameworkReferences,
            CompilationOptions);

        using MemoryStream image = new();
        var result = stubs.Emit(image);
        if (!result.Success)
            throw new InvalidOperationException(
                "The contract stubs do not compile: " + string.Join(Environment.NewLine, result.Diagnostics));

        return new RoslynEnvironment(frameworkReferences, [.. image.ToArray()]);
    }
}
