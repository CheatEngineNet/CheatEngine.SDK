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

	private const string SourceRelativePath = "native/cheatengine-sdk-lua-bridge/cheatengine_sdk_lua_bridge.c";
	private const string BuildRelativePath = "native/cheatengine-sdk-lua-bridge/xmake.lua";
	private const string ContinuousIntegrationWorkflowRelativePath = ".github/workflows/ci.yml";
	private const string PinnedXmakeVersion = "3.0.9";
	private const string PinnedMsvcToolset = "14.44";
	private const string PinnedWindowsSdk = "10.0.26100.0";
	private const string PinnedWindowsRunner = "windows-2025";

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
	public void Checked_in_bridge_exports_the_current_source_and_xmake_fingerprint()
	{
		string sourceHash = CalculateSha256(RepositoryLayout.PathOf(SourceRelativePath));
		string buildHash = CalculateSha256(RepositoryLayout.PathOf(BuildRelativePath));
		string expectedFingerprint = $"{sourceHash}:{buildHash}";
		Assert.Equal(expectedFingerprint,
			ReadBridge().ReadExportedAsciiZ("cheatengine_sdk_lua_bridge_source_fingerprint"));
	}

	[Fact]
	public void Native_bridge_xmake_configuration_pins_the_required_compilation_contract()
	{
		string xmakeConfiguration = ReadRepositoryText(BuildRelativePath);

		Assert.Contains("set_languages(\"c11\")", xmakeConfiguration, StringComparison.Ordinal);
		Assert.Contains("set_warnings(\"allextra\", \"error\")", xmakeConfiguration, StringComparison.Ordinal);
		Assert.Contains("set_toolchains(\"msvc\")", xmakeConfiguration, StringComparison.Ordinal);
		Assert.Contains("set_policy(\"build.c++.msvc.runtime\", \"MT\")", xmakeConfiguration,
			StringComparison.Ordinal);
		Assert.Contains("set_runtimes(\"MT\")", xmakeConfiguration, StringComparison.Ordinal);
		Assert.Contains("add_cflags(\"/MT\", {tools = \"cl\", force = true})", xmakeConfiguration,
			StringComparison.Ordinal);
		Assert.Contains("add_shflags(\"/Brepro\", {tools = \"link\", force = true})", xmakeConfiguration,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Native_bridge_ci_builds_once_with_pinned_xmake_and_reports_its_sha256()
	{
		string job = ReadNativeJob();

		// The native job installs the pinned xmake and configures/builds the bridge directly (no bespoke eng/ci
		// wrapper script): one build feeds every downstream job, and its SHA-256 becomes a job output.
		Assert.Contains("xmake-io/github-action-setup-xmake@", job, StringComparison.Ordinal);
		Assert.Contains($"xmake-version: '{PinnedXmakeVersion}'", job, StringComparison.Ordinal);
		Assert.Contains(
			"xmake f -P native/cheatengine-sdk-lua-bridge -o artifacts/native/cheatengine-sdk-lua-bridge -p windows -a x64 -m release -y --ccache=n",
			job, StringComparison.Ordinal);
		Assert.Contains("xmake -P native/cheatengine-sdk-lua-bridge -y", job, StringComparison.Ordinal);
		Assert.Contains("path: artifacts/native/cheatengine-sdk-lua-bridge/cheatengine-sdk-lua-bridge.dll", job,
			StringComparison.Ordinal);
		Assert.Contains("bridge-sha256: ${{ steps.bridge.outputs.bridge-sha256 }}", job, StringComparison.Ordinal);
		Assert.Contains("\"bridge-sha256=$hash\"", job, StringComparison.Ordinal);

		// Every build compiles from source: a compiler-cache hit must not stand in for a real compilation.
		Assert.Contains("--ccache=n", job, StringComparison.Ordinal);
	}

	[Fact]
	public void Native_bridge_ci_pins_the_msvc_toolset_and_windows_sdk()
	{
		string job = ReadNativeJob();

		Assert.Contains($"BRIDGE_VS_TOOLSET: '{PinnedMsvcToolset}'", job, StringComparison.Ordinal);
		Assert.Contains($"BRIDGE_VS_SDKVER: '{PinnedWindowsSdk}'", job, StringComparison.Ordinal);
		Assert.Contains("\"--vs_toolset=$env:BRIDGE_VS_TOOLSET\" \"--vs_sdkver=$env:BRIDGE_VS_SDKVER\"", job,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Native_bridge_ci_runs_on_the_pinned_windows_label()
	{
		string job = ReadNativeJob();

		Assert.Contains($"runs-on: {PinnedWindowsRunner}\n", job, StringComparison.Ordinal);
		Assert.DoesNotContain("-latest", job, StringComparison.Ordinal);
	}

	private static PortableExecutableInspector ReadBridge()
	{
		return PortableExecutableInspector.Read(BridgePath);
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

	/// <summary>Reads a committed text file with LF line endings, whatever the checkout's line-ending conversion.</summary>
	private static string ReadRepositoryText(string relativePath)
	{
		return File.ReadAllText(RepositoryLayout.PathOf(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	/// <summary>
	///     The <c>native</c> job of ci.yml: from its key to the next line indented like a job key (the next job, or the
	///     comment that introduces it), so an assertion cannot be satisfied by text of another job.
	/// </summary>
	private static string ReadNativeJob()
	{
		string[] lines = ReadRepositoryText(ContinuousIntegrationWorkflowRelativePath).Split('\n');
		int start = Array.IndexOf(lines, "  native:");
		Assert.True(start >= 0, $"{ContinuousIntegrationWorkflowRelativePath} has no 'native' job.");

		int end = start + 1;
		while (end < lines.Length && !IsJobLevelLine(lines[end]))
		{
			end++;
		}

		return string.Join('\n', lines, start, end - start) + "\n";
	}

	private static bool IsJobLevelLine(string line)
	{
		return line.Length > 2 && line.StartsWith("  ", StringComparison.Ordinal) && line[2] != ' ';
	}

	private static string CalculateSha256(string path)
	{
		return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
	}
}
