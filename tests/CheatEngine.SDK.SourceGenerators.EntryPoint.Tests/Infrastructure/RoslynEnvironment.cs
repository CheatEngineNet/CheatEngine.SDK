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

    private RoslynEnvironment(
        ImmutableArray<MetadataReference> frameworkReferences,
        ImmutableArray<byte> annotationsImage,
        ImmutableArray<byte> hostingImage)
    {
        FrameworkReferences = frameworkReferences;
        AnnotationsImage = annotationsImage;
        HostingImage = hostingImage;
        AnnotationsReference = MetadataReference.CreateFromImage(
            annotationsImage,
            filePath: ContractStubs.AnnotationsAssemblyName + ".dll");
        HostingReference =
            MetadataReference.CreateFromImage(hostingImage, filePath: ContractStubs.HostingAssemblyName + ".dll");
    }

    /// <summary>The process-wide environment over <see cref="LocalFrameworkReferences.Load" />.</summary>
    public static RoslynEnvironment Shared => LazyShared.Value;

    /// <summary><c>Microsoft.NETCore.App</c> 10.0: reference assemblies, or the running runtime as a fallback.</summary>
    public ImmutableArray<MetadataReference> FrameworkReferences { get; }

    /// <summary>The compiled annotations contract, to load for execution tests.</summary>
    public ImmutableArray<byte> AnnotationsImage { get; }

    /// <summary>The compiled hosting contract, to load for execution tests.</summary>
    public ImmutableArray<byte> HostingImage { get; }

    /// <summary>The annotations contract, as a compilation reference.</summary>
    public MetadataReference AnnotationsReference { get; }

    /// <summary>The hosting contract, as a compilation reference.</summary>
    public MetadataReference HostingReference { get; }

    /// <summary>Framework + SDK contract assemblies: the references of a plugin compilation.</summary>
    public ImmutableArray<MetadataReference> PluginReferences =>
        FrameworkReferences.Add(AnnotationsReference).Add(HostingReference);

    /// <summary>Compiles the contract stubs against <paramref name="frameworkReferences" />.</summary>
    /// <exception cref="InvalidOperationException">The stubs do not compile against these references.</exception>
    public static RoslynEnvironment Create(ImmutableArray<MetadataReference> frameworkReferences)
    {
        var annotations = CSharpCompilation.Create(
            ContractStubs.AnnotationsAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ContractStubs.AnnotationsSource, ParseOptions, "AnnotationsStubs.cs",
                    cancellationToken: TestContext.Current.CancellationToken)
            ],
            frameworkReferences,
            CompilationOptions);
        var hosting = CSharpCompilation.Create(
            ContractStubs.HostingAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ContractStubs.HostingSource, ParseOptions, "HostingStubs.cs",
                    cancellationToken: TestContext.Current.CancellationToken)
            ],
            frameworkReferences,
            CompilationOptions);

        return new RoslynEnvironment(
            frameworkReferences,
            EmitImage(annotations, ContractStubs.AnnotationsAssemblyName),
            EmitImage(hosting, ContractStubs.HostingAssemblyName));
    }

    private static ImmutableArray<byte> EmitImage(CSharpCompilation compilation, string assemblyName)
    {
        using MemoryStream image = new();
        var result = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        if (!result.Success)
            throw new InvalidOperationException(
                "The " + assemblyName + " contract stubs do not compile: " +
                string.Join(Environment.NewLine, result.Diagnostics));

        return [.. image.ToArray()];
    }
}
