using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The plugin folders the solution builds for the live Cheat Engine runs (<c>tests/CheatEngine.SDK.LivePlugin</c> and
///     the two coexistence plugins) are deployable units on their own: everything their <c>.deps.json</c> promises is in
///     the folder, the runtime policy is the <c>net10.0</c> / <c>Microsoft.NETCore.App</c> profile Cheat Engine's hostfxr
///     loader reads, no Lua runtime is copied, and the native bridge is byte-identical to the workspace bridge (the
///     CI-built one in CI). This measures the "plugin output" link of the release tuple (audit ch.09, A09-13) at C1 only:
///     these bundles are built from source through project references, so <c>project</c>-typed SDK libraries are expected
///     here, unlike in the package consumers of <see cref="CleanConsumerIsolationTests" />. The folders of the running
///     test configuration are read; the tests run in both CI legs and never skip.
/// </summary>
public sealed class LivePluginClosureTests
{
	private const string BridgeFile = "cheatengine-sdk-lua-bridge.dll";
	private const string WorkspaceBridge = "native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/" + BridgeFile;

	/// <summary>The SDK assemblies the managed bootstrap loads before any plugin code runs.</summary>
	private static readonly string[] s_bootstrapLibraries =
	[
		"CheatEngine.SDK.Abi", "CheatEngine.SDK.Annotations", "CheatEngine.SDK.Hosting", "CheatEngine.SDK.Lua",
		"CheatEngine.SDK.Lua.Interop"
	];

	/// <summary>
	///     Bundles built without the Cheat Engine entry point. The entry-point generator emits <c>CESDK.CESDK</c> only when
	///     <c>CheatEngineSdkGenerateEntryPoint</c> is compiler-visible and true, which the package's
	///     <c>build/CheatEngine.SDK.props</c> gives a direct consumer. <c>CheatEngine.SDK.LivePlugin</c> references the
	///     libraries and the generator as projects and sets neither, so Cheat Engine finds no entry point in it. The
	///     coexistence fixtures set both directly in their own <c>CoexistencePlugin.props</c>, so they are no longer
	///     pending. The list only shrinks: a bundle that gains the entry point fails until it is removed from here.
	/// </summary>
	private static readonly HashSet<string> s_pendingEntryPoint = new(StringComparer.Ordinal)
	{
		"CheatEngine.SDK.LivePlugin"
	};

	[Theory]
	[InlineData("CheatEngine.SDK.LivePlugin")]
	[InlineData("CheatEngine.SDK.LivePlugin.Coexistence.PluginA")]
	[InlineData("CheatEngine.SDK.LivePlugin.Coexistence.PluginB")]
	public void Live_plugin_output_closure_matches_the_workspace_bridge(string project)
	{
		string folder = BundleFolder(project);

		// Everything the dependency manifest promises is deployed, and no SDK assembly is deployed without being listed.
		using JsonDocument deps = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, project + ".deps.json")));
		JsonElement target = Assert.Single(deps.RootElement.GetProperty("targets").EnumerateObject()).Value;
		HashSet<string> promised = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> sdkLibraries = new(StringComparer.Ordinal);
		foreach (JsonProperty library in target.EnumerateObject())
		{
			string name = library.Name[..library.Name.IndexOf('/', StringComparison.Ordinal)];
			if (name.StartsWith("CheatEngine.SDK.", StringComparison.Ordinal) && !string.Equals(name, project, StringComparison.Ordinal))
			{
				sdkLibraries.Add(name);
			}

			if (library.Value.TryGetProperty("runtime", out JsonElement runtime))
			{
				foreach (JsonProperty asset in runtime.EnumerateObject())
				{
					promised.Add(Path.GetFileName(asset.Name));
				}
			}
		}

		Assert.Superset(new HashSet<string>(s_bootstrapLibraries, StringComparer.Ordinal), sdkLibraries);
		foreach (string file in promised)
		{
			Assert.True(File.Exists(Path.Combine(folder, file)), $"{project}.deps.json promises {file}, which is not in {folder}.");
		}

		foreach (string assembly in Directory.EnumerateFiles(folder, "CheatEngine.SDK.*.dll"))
		{
			Assert.True(promised.Contains(Path.GetFileName(assembly)),
				$"{Path.GetFileName(assembly)} is deployed in {folder} but {project}.deps.json does not list it.");
		}

		// The runtime policy is the one the managed hostfxr profile loads: net10.0 on the shared Microsoft.NETCore.App.
		using JsonDocument runtimeConfig = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, project + ".runtimeconfig.json")));
		JsonElement options = runtimeConfig.RootElement.GetProperty("runtimeOptions");
		Assert.Equal("net10.0", options.GetProperty("tfm").GetString());
		Assert.Equal("Microsoft.NETCore.App", options.GetProperty("framework").GetProperty("name").GetString());
		Assert.False(options.TryGetProperty("includedFrameworks", out _), $"{project} must not be self-contained.");

		// The bridge is the workspace one, and no Lua runtime travels with the plugin (Cheat Engine's own is used).
		string bridge = Path.Combine(folder, BridgeFile);
		Assert.True(File.Exists(bridge), $"{folder} has no {BridgeFile}.");
		Assert.Equal(Sha256(RepositoryLayout.PathOf(WorkspaceBridge)), Sha256(bridge));
		foreach (string file in Directory.EnumerateFiles(folder, "*.dll"))
		{
			string name = Path.GetFileName(file);
			Assert.False(name.StartsWith("lua", StringComparison.OrdinalIgnoreCase), $"{folder} deploys a Lua runtime, {name}.");
		}
	}

	[Theory]
	[InlineData("CheatEngine.SDK.LivePlugin")]
	[InlineData("CheatEngine.SDK.LivePlugin.Coexistence.PluginA")]
	[InlineData("CheatEngine.SDK.LivePlugin.Coexistence.PluginB")]
	public void Live_plugin_entry_point_is_present_unless_listed_as_a_pending_fix(string project)
	{
		string assembly = Path.Combine(BundleFolder(project), project + ".dll");

		string? signature = FindEntryPointSignature(assembly);

		if (s_pendingEntryPoint.Contains(project))
		{
			Assert.True(signature is null,
				$"{project} now declares CESDK.CESDK.CEPluginInitialize: remove it from {nameof(s_pendingEntryPoint)}.");
			return;
		}

		Assert.Equal("public static int CEPluginInitialize(System.IntPtr, int)", signature);
	}

	/// <summary>
	///     The folder of <paramref name="project" /> for the configuration of the running test assembly, which lives in
	///     <c>artifacts/bin/CheatEngine.SDK.Tests/&lt;configuration&gt;/</c>.
	/// </summary>
	private static string BundleFolder(string project)
	{
		string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
		string folder = RepositoryLayout.PathOf($"artifacts/bin/{project}/{configuration}");
		Assert.True(File.Exists(Path.Combine(folder, project + ".dll")),
			$"{project} has no {configuration} output in {folder}: build CheatEngine.SDK.slnx -c {configuration} first.");
		return folder;
	}

	/// <summary>The accessibility, return type and parameters of <c>CESDK.CESDK.CEPluginInitialize</c>, or null without it.</summary>
	private static string? FindEntryPointSignature(string assemblyPath)
	{
		using FileStream stream = File.OpenRead(assemblyPath);
		using PEReader peReader = new(stream);
		MetadataReader reader = peReader.GetMetadataReader();
		foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
		{
			TypeDefinition type = reader.GetTypeDefinition(typeHandle);
			if (!string.Equals(reader.GetString(type.Namespace), "CESDK", StringComparison.Ordinal)
				|| !string.Equals(reader.GetString(type.Name), "CESDK", StringComparison.Ordinal))
			{
				continue;
			}

			foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
			{
				MethodDefinition method = reader.GetMethodDefinition(methodHandle);
				if (!string.Equals(reader.GetString(method.Name), "CEPluginInitialize", StringComparison.Ordinal))
				{
					continue;
				}

				MethodSignature<string> decoded = method.DecodeSignature(SignatureNames.Instance, genericContext: null);
				string access = (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public ? "public" : "non-public";
				string binding = (method.Attributes & MethodAttributes.Static) != MethodAttributes.PrivateScope ? "static" : "instance";
				return $"{access} {binding} {decoded.ReturnType} CEPluginInitialize({string.Join(", ", decoded.ParameterTypes)})";
			}
		}

		return null;
	}

	private static string Sha256(string path)
	{
		return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
	}

	/// <summary>Names signature types the way the assertion spells them: C# keywords for the primitives the entry point uses.</summary>
	private sealed class SignatureNames : ISignatureTypeProvider<string, object?>
	{
		public static readonly SignatureNames Instance = new();

		public string GetPrimitiveType(PrimitiveTypeCode typeCode)
		{
			return typeCode switch
			{
				PrimitiveTypeCode.Int32 => "int",
				PrimitiveTypeCode.Void => "void",
				PrimitiveTypeCode.IntPtr => "System.IntPtr",
				_ => typeCode.ToString()
			};
		}

		public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
		{
			TypeDefinition definition = reader.GetTypeDefinition(handle);
			return reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name);
		}

		public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
		{
			TypeReference reference = reader.GetTypeReference(handle);
			return reader.GetString(reference.Namespace) + "." + reader.GetString(reference.Name);
		}

		public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
		{
			return "typespec";
		}

		public string GetSZArrayType(string elementType)
		{
			return elementType + "[]";
		}

		public string GetArrayType(string elementType, ArrayShape shape)
		{
			return elementType + "[" + new string(',', shape.Rank - 1) + "]";
		}

		public string GetByReferenceType(string elementType)
		{
			return "ref " + elementType;
		}

		public string GetPointerType(string elementType)
		{
			return elementType + "*";
		}

		public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
		{
			return genericType + "<" + string.Join(", ", typeArguments) + ">";
		}

		public string GetGenericMethodParameter(object? genericContext, int index)
		{
			return "!!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		public string GetGenericTypeParameter(object? genericContext, int index)
		{
			return "!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
		{
			return unmodifiedType;
		}

		public string GetPinnedType(string elementType)
		{
			return elementType;
		}

		public string GetFunctionPointerType(MethodSignature<string> signature)
		{
			return "delegate*";
		}
	}
}
