using System.Collections.Immutable;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     What every test compilation needs and what is expensive to build: the <c>net10.0</c> framework references and the
///     real <c>CheatEngine.SDK.Lua</c> (+ <c>CheatEngine.SDK.Lua.Interop</c>, <c>CheatEngine.SDK.Annotations</c>) assembly
///     the generated wrappers are
///     written against, taken from the copies loaded in this test process so that an emitted assembly, once loaded,
///     binds to the very same types. Built once per process, from the local disk only.
/// </summary>
internal sealed class RoslynEnvironment
{
	/// <summary>Documentation comments are parsed and diagnosed, like in a project with <c>GenerateDocumentationFile</c>.</summary>
	public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14, DocumentationMode.Diagnose);

	/// <summary>Strict: nullable on, every warning wave. EngineApi bodies contain no unsafe code, so unsafe stays off.</summary>
	public static readonly CSharpCompilationOptions CompilationOptions = new(
		OutputKind.DynamicallyLinkedLibrary,
		nullableContextOptions: NullableContextOptions.Enable,
		warningLevel: 9999);

	private static readonly Lazy<RoslynEnvironment> LazyShared =
		new(static () => new RoslynEnvironment(LocalFrameworkReferences.Load()));

	private RoslynEnvironment(ImmutableArray<MetadataReference> frameworkReferences)
	{
		FrameworkReferences = frameworkReferences;
		SdkReferences =
		[
			MetadataReference.CreateFromFile(typeof(LuaFunctionAttribute).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(LuaApi).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(LuaState).Assembly.Location),

			// Not a real CheatEngine.SDK.Engine reference (this project deliberately does not reference it): this test
			// assembly's own file, so a generated wrapper's 'global::CheatEngine.SDK.Engine.Values.Address'
			// resolves to Infrastructure/Address.cs (declared in namespace CheatEngine.SDK.Engine.Values), loaded a second
			// time from the same file by the custom AssemblyLoadContext's fallback to the default context
			// (GeneratedAssembly's own doc comment).
			MetadataReference.CreateFromFile(typeof(Address).Assembly.Location)
		];
	}

	/// <summary>The process-wide environment.</summary>
	public static RoslynEnvironment Shared => LazyShared.Value;

	/// <summary><c>Microsoft.NETCore.App</c> 10.0: reference assemblies, or the running runtime as a fallback.</summary>
	public ImmutableArray<MetadataReference> FrameworkReferences
	{
		get;
	}

	/// <summary>
	///     The real <c>CheatEngine.SDK.Annotations</c>, <c>CheatEngine.SDK.Lua.Interop</c> and <c>CheatEngine.SDK.Lua</c>, as
	///     loaded in this process,
	///     plus this test assembly's own file for the <see cref="Address" /> stub (see its own doc comment).
	/// </summary>
	public ImmutableArray<MetadataReference> SdkReferences
	{
		get;
	}

	/// <summary>Framework + SDK: the references of a compilation the generated wrappers land in.</summary>
	public ImmutableArray<MetadataReference> PluginReferences => FrameworkReferences.AddRange(SdkReferences);
}
