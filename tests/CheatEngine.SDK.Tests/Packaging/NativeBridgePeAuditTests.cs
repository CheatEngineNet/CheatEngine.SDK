using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Guards the deliberately tiny native protection boundary without executing it: the checked-in bridge must remain
///     an AMD64 PE32+ DLL, export only its four managed contracts, and never acquire a Lua runtime through normal or
///     delay-loaded imports. The packaged direct-consumer copies must be byte-identical to that audited source asset.
/// </summary>
public sealed class NativeBridgePeAuditTests
{
	private const string BridgeRelativePath =
		"native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll";

	private const string ManifestRelativePath = "native/cheatengine-sdk-lua-bridge/bridge-audit-manifest.json";
	private const string SourceRelativePath = "native/cheatengine-sdk-lua-bridge/cheatengine_sdk_lua_bridge.c";
	private const string BuildRelativePath = "native/cheatengine-sdk-lua-bridge/xmake.lua";

	private static readonly string[] ExpectedExports =
	[
		"cheatengine_sdk_lua_bridge_abi_version",
		"cheatengine_sdk_lua_bridge_get_contract",
		"cheatengine_sdk_lua_bridge_source_fingerprint",
		"cheatengine_sdk_lua_protected"
	];

	private static readonly string[] s_allowedImportModules = ["KERNEL32.dll"];

	private static string BridgePath => RepositoryLayout.PathOf(BridgeRelativePath);

	[Fact]
	public void Checked_in_bridge_is_an_amd64_PE32_plus_dll_with_the_exact_export_surface()
	{
		PortableExecutableInspector image = ReadBridge();

		Assert.Equal(PEMagic.PE32Plus, image.Magic);
		Assert.Equal(Machine.Amd64, image.Machine);
		Assert.True(image.IsDll);
		AssertExactSet(ExpectedExports, GetExportNames(image));
	}

	[Fact]
	public void Checked_in_bridge_imports_only_kernel32_without_delay_load()
	{
		PortableExecutableInspector image = ReadBridge();

		Assert.False(image.HasDelayImports, "The bridge must not carry a delay-load directory.");

		IReadOnlyList<PortableExecutableImport> imports = image.GetImports();
		Assert.Equal(s_allowedImportModules.Length, imports.Count);
		HashSet<string> observedModules = new(StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < imports.Count; index++)
		{
			PortableExecutableImport import = imports[index];
			Assert.True(observedModules.Add(import.ModuleName),
				$"The import directory has duplicate module '{import.ModuleName}'.");
			Assert.False(import.ModuleName.Contains("lua", StringComparison.OrdinalIgnoreCase),
				$"The bridge must not import a Lua module ('{import.ModuleName}').");
			Assert.Contains(import.ModuleName, s_allowedImportModules, StringComparer.OrdinalIgnoreCase);

			for (int symbolIndex = 0; symbolIndex < import.Symbols.Count; symbolIndex++)
			{
				Assert.False(import.Symbols[symbolIndex].Contains("lua", StringComparison.OrdinalIgnoreCase),
					$"The bridge must not import a Lua symbol ('{import.Symbols[symbolIndex]}').");
			}
		}

		foreach (string moduleName in s_allowedImportModules)
		{
			Assert.Contains(moduleName, observedModules, StringComparer.OrdinalIgnoreCase);
		}
	}

	[Fact]
	public void Audit_manifest_records_the_pinned_toolchain_flags_and_hashes()
	{
		using JsonDocument manifest = ReadManifest();
		JsonElement root = manifest.RootElement;

		Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
		Assert.Equal("xmake", root.GetProperty("toolchain").GetProperty("buildSystem").GetProperty("name").GetString());
		Assert.Equal("3.0.9",
			root.GetProperty("toolchain").GetProperty("buildSystem").GetProperty("version").GetString());
		Assert.Equal("MSVC", root.GetProperty("toolchain").GetProperty("compiler").GetProperty("name").GetString());
		Assert.Equal("C11", root.GetProperty("toolchain").GetProperty("languageStandard").GetString());
		Assert.Equal("MT", root.GetProperty("toolchain").GetProperty("cRuntime").GetString());
		AssertJsonStringSet(root.GetProperty("toolchain").GetProperty("compilerFlags"), ["/MT", "allextra", "error"]);
		AssertJsonStringSet(root.GetProperty("toolchain").GetProperty("linkerFlags"), ["/Brepro"]);
		Assert.True(root.GetProperty("reproducibility").GetProperty("doubleBuildSha256Comparison").GetBoolean());

		JsonElement nativeAsset = root.GetProperty("nativeAsset");
		Assert.Equal("PE32+", nativeAsset.GetProperty("pe").GetProperty("format").GetString());
		Assert.Equal("AMD64", nativeAsset.GetProperty("pe").GetProperty("machine").GetString());
		Assert.True(nativeAsset.GetProperty("pe").GetProperty("isDll").GetBoolean());
		AssertJsonStringSet(nativeAsset.GetProperty("exports"), ExpectedExports);
		AssertJsonStringSet(nativeAsset.GetProperty("delayImports"), []);
		AssertJsonStringSet(nativeAsset.GetProperty("imports"), s_allowedImportModules);

		string sourceHash = CalculateSha256(RepositoryLayout.PathOf(SourceRelativePath));
		string buildHash = CalculateSha256(RepositoryLayout.PathOf(BuildRelativePath));
		string expectedFingerprint = $"{sourceHash}:{buildHash}";
		Assert.Equal(sourceHash,
			root.GetProperty("source").GetProperty("hashes").GetProperty("cheatengine_sdk_lua_bridge.c").GetString());
		Assert.Equal(buildHash, root.GetProperty("source").GetProperty("hashes").GetProperty("xmake.lua").GetString());
		Assert.Equal(expectedFingerprint, root.GetProperty("source").GetProperty("fingerprint").GetString());

		string bridgeHash = CalculateSha256(BridgePath);
		Assert.Equal(bridgeHash, nativeAsset.GetProperty("sha256").GetString());
		Assert.Equal(expectedFingerprint,
			ReadBridge().ReadExportedAsciiZ("cheatengine_sdk_lua_bridge_source_fingerprint"));
	}

	private static PortableExecutableInspector ReadBridge()
	{
		return PortableExecutableInspector.Read(BridgePath);
	}

	private static JsonDocument ReadManifest()
	{
		return JsonDocument.Parse(File.ReadAllText(RepositoryLayout.PathOf(ManifestRelativePath)));
	}

	private static List<string> GetExportNames(PortableExecutableInspector image)
	{
		IReadOnlyList<PortableExecutableExport> exports = image.GetExports();
		List<string> names = new(exports.Count);
		for (int index = 0; index < exports.Count; index++)
		{
			names.Add(exports[index].Name);
		}

		return names;
	}

	private static void AssertExactSet(string[] expected, List<string> actual)
	{
		Assert.Equal(expected.Length, actual.Count);
		HashSet<string> actualValues = new(actual, StringComparer.Ordinal);
		Assert.Equal(actual.Count, actualValues.Count);
		for (int index = 0; index < expected.Length; index++)
		{
			Assert.Contains(expected[index], actualValues, StringComparer.Ordinal);
		}
	}

	private static void AssertJsonStringSet(JsonElement array, string[] expected)
	{
		Assert.Equal(JsonValueKind.Array, array.ValueKind);
		Assert.Equal(expected.Length, array.GetArrayLength());
		HashSet<string> values = new(StringComparer.Ordinal);
		foreach (JsonElement entry in array.EnumerateArray())
		{
			values.Add(entry.GetString() ?? string.Empty);
		}

		Assert.Equal(expected.Length, values.Count);
		for (int index = 0; index < expected.Length; index++)
		{
			Assert.Contains(expected[index], values, StringComparer.Ordinal);
		}
	}

	private static string CalculateSha256(string path)
	{
		return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
	}
}
