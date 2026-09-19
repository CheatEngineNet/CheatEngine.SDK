using System.Collections.Immutable;
using CESDK.Annotations.Lua;
using CESDK.Lua.Interop.Api;
using CESDK.Lua.State;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CESDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>
///     What every test compilation needs and what is expensive to build: the <c>net10.0</c> framework references and
///     the three real SDK assemblies the generated code is written against (<c>CESDK.Annotations</c>,
///     <c>CESDK.Lua.Interop</c>, <c>CESDK.Lua</c>), taken from the copies loaded in this test process so that an
///     emitted assembly, once loaded, binds to the very same types. Built once per process, from the local disk only.
/// </summary>
internal sealed class RoslynEnvironment
{
    /// <summary>Documentation comments are parsed and diagnosed, like in a project with <c>GenerateDocumentationFile</c>.</summary>
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14, DocumentationMode.Diagnose);

    /// <summary>Strict, with unsafe ON: the registration table takes thunk addresses. Nullable on, every warning wave.</summary>
    public static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable,
        allowUnsafe: true,
        warningLevel: 9999);

    /// <summary>The same options with unsafe OFF: the generator must then emit nothing.</summary>
    public static readonly CSharpCompilationOptions SafeCompilationOptions = CompilationOptions.WithAllowUnsafe(false);

    private static readonly Lazy<RoslynEnvironment> LazyShared =
        new(static () => new RoslynEnvironment(LocalFrameworkReferences.Load()));

    private RoslynEnvironment(ImmutableArray<MetadataReference> frameworkReferences)
    {
        FrameworkReferences = frameworkReferences;
        SdkReferences =
        [
            MetadataReference.CreateFromFile(typeof(LuaFunctionAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(LuaApi).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(LuaState).Assembly.Location)
        ];
    }

    /// <summary>The process-wide environment.</summary>
    public static RoslynEnvironment Shared => LazyShared.Value;

    /// <summary><c>Microsoft.NETCore.App</c> 10.0: reference assemblies, or the running runtime as a fallback.</summary>
    public ImmutableArray<MetadataReference> FrameworkReferences { get; }

    /// <summary>The real <c>CESDK.Annotations</c>, <c>CESDK.Lua.Interop</c> and <c>CESDK.Lua</c>, as loaded in this process.</summary>
    public ImmutableArray<MetadataReference> SdkReferences { get; }

    /// <summary>Framework + SDK: the references of a plugin compilation.</summary>
    public ImmutableArray<MetadataReference> PluginReferences => FrameworkReferences.AddRange(SdkReferences);
}
