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
	private const string NativeBuildScriptRelativePath = "eng/ci/Build-NativeBridge.ps1";
	private const string PinnedXmakeVersion = "3.0.9";
	private const string PinnedMsvcToolset = "14.44";
	private const string PinnedWindowsSdk = "10.0.26100.0";
	private const string PinnedWindowsRunner = "windows-2025";

	/// <summary>The job outputs build-info.json is written from (shared contract 1.6), plus the resolved toolset folder.</summary>
	private static readonly string[] s_toolchainOutputs =
	[
		"image-version",
		"xmake-version",
		"msvc-toolset",
		"msvc-version",
		"windows-sdk-version",
		"bridge-sha256",
		"bridge-fingerprint",
		"checked-in-bridge-sha256"
	];

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
	public void Native_bridge_ci_pins_xmake_and_enforces_a_double_build_reproducibility_gate()
	{
		string job = ReadNativeJob();
		string script = ReadRepositoryText(NativeBuildScriptRelativePath);

		// The native job installs the pinned xmake and delegates the builds to the script, which runs the same locally.
		Assert.Contains("xmake-io/github-action-setup-xmake@", job, StringComparison.Ordinal);
		Assert.Contains($"xmake-version: '{PinnedXmakeVersion}'", job, StringComparison.Ordinal);
		Assert.Contains(
			"./eng/ci/Build-NativeBridge.ps1 -VsToolset $env:BRIDGE_VS_TOOLSET -VsSdkVersion $env:BRIDGE_VS_SDKVER",
			job, StringComparison.Ordinal);
		Assert.Contains("path: artifacts/native/cheatengine-sdk-lua-bridge/cheatengine-sdk-lua-bridge.dll", job,
			StringComparison.Ordinal);

		Assert.Contains("$projectDirectory = 'native/cheatengine-sdk-lua-bridge'", script, StringComparison.Ordinal);
		Assert.Contains("$primaryOutput = 'artifacts/native/cheatengine-sdk-lua-bridge'", script,
			StringComparison.Ordinal);
		Assert.Contains("$reproducibilityOutput = 'artifacts/native/cheatengine-sdk-lua-bridge-repro'", script,
			StringComparison.Ordinal);
		Assert.Contains("The primary and reproducibility bridge output directories must be distinct.", script,
			StringComparison.Ordinal);
		Assert.Contains("& $Xmake f -P $projectDirectory -o $OutputDirectory", script, StringComparison.Ordinal);
		Assert.Contains("$primaryBridge = Invoke-BridgeBuild -OutputDirectory $primaryOutput", script,
			StringComparison.Ordinal);
		Assert.Contains("$reproducibilityBridge = Invoke-BridgeBuild -OutputDirectory $reproducibilityOutput", script,
			StringComparison.Ordinal);
		Assert.Contains("$primaryHash = Get-Sha256 -Path $primaryBridge", script, StringComparison.Ordinal);
		Assert.Contains("$reproducibilityHash = Get-Sha256 -Path $reproducibilityBridge", script,
			StringComparison.Ordinal);
		Assert.Contains("$primaryHash, $reproducibilityHash, [StringComparison]::OrdinalIgnoreCase", script,
			StringComparison.Ordinal);
		// Every build compiles from source: a compiler-cache hit must not stand in for a second compilation.
		Assert.Contains("--ccache=n", script, StringComparison.Ordinal);
	}

	[Fact]
	public void Native_bridge_ci_pins_the_msvc_toolset_and_windows_sdk()
	{
		string job = ReadNativeJob();
		string script = ReadRepositoryText(NativeBuildScriptRelativePath);

		Assert.Contains($"BRIDGE_VS_TOOLSET: '{PinnedMsvcToolset}'", job, StringComparison.Ordinal);
		Assert.Contains($"BRIDGE_VS_SDKVER: '{PinnedWindowsSdk}'", job, StringComparison.Ordinal);

		// The one xmake configure line of the script passes both pins, for the primary, reproducibility and path builds.
		Assert.Single(script.Split('\n'), static line => line.Contains("& $Xmake f ", StringComparison.Ordinal));
		Assert.Contains("\"--vs_toolset=$VsToolset\" \"--vs_sdkver=$VsSdkVersion\"", script, StringComparison.Ordinal);

		// A pin xmake silently ignored would still build: the script compares what xmake resolved with the pins.
		Assert.Contains("xmake resolved MSVC toolset $msvcToolset although --vs_toolset=$VsToolset was requested.",
			script, StringComparison.Ordinal);
		Assert.Contains("xmake resolved Windows SDK $windowsSdk although --vs_sdkver=$VsSdkVersion was requested.",
			script, StringComparison.Ordinal);
		// The bytes agree: the PE header of the CI-built DLL records the linker of the resolved toolset.
		Assert.Contains("$linkerVersion = Get-LinkerVersion -Path $primaryBridge", script, StringComparison.Ordinal);
		Assert.Contains("records linker $linkerVersion in its PE header, but xmake resolved MSVC toolset $msvcToolset.", script,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Native_bridge_ci_rebuilds_from_a_copied_tree_and_compares_hashes()
	{
		string job = ReadNativeJob();
		string script = ReadRepositoryText(NativeBuildScriptRelativePath);

		Assert.Contains("-PathCheckRoot (Join-Path $env:RUNNER_TEMP 'bridge-path-check')", job, StringComparison.Ordinal);

		// Only the two fingerprinted build inputs are copied, byte for byte, and built from inside the copy with a
		// relative output path (xmake 3.0.9 mis-parses an absolute -o).
		Assert.Contains("$buildInputs = @('cheatengine_sdk_lua_bridge.c', 'xmake.lua')", script, StringComparison.Ordinal);
		Assert.Contains("$pathCheckOutput = 'artifacts/native/path-check'", script, StringComparison.Ordinal);
		Assert.Contains("Copy-Item -LiteralPath (Join-Path $repositoryRoot \"$projectDirectory/$inputFile\")", script,
			StringComparison.Ordinal);
		Assert.Contains("Push-Location -LiteralPath $resolvedPathCheckRoot", script, StringComparison.Ordinal);
		Assert.Contains("$pathCheckBridge = Invoke-BridgeBuild -OutputDirectory $pathCheckOutput", script,
			StringComparison.Ordinal);
		Assert.Contains("[string]::Equals($primaryHash, $pathCheckHash, [StringComparison]::OrdinalIgnoreCase)", script,
			StringComparison.Ordinal);
		Assert.Contains("must be outside the repository", script, StringComparison.Ordinal);
	}

	[Fact]
	public void Native_bridge_ci_runs_on_the_pinned_windows_label()
	{
		string job = ReadNativeJob();

		Assert.Contains($"runs-on: {PinnedWindowsRunner}\n", job, StringComparison.Ordinal);
		Assert.DoesNotContain("-latest", job, StringComparison.Ordinal);
	}

	[Fact]
	public void Native_bridge_ci_publishes_the_toolchain_facts_as_job_outputs()
	{
		string job = ReadNativeJob();
		string script = ReadRepositoryText(NativeBuildScriptRelativePath);

		foreach (string output in s_toolchainOutputs)
		{
			Assert.Contains($"{output}: ${{{{ steps.bridge.outputs.{output} }}}}", job, StringComparison.Ordinal);
			Assert.Contains($"\"{output}=", script, StringComparison.Ordinal);
		}

		// Byte drift from the checked-in DLL is a notice, never a failure (shared contract 1.6).
		Assert.Contains("::notice title=Native bridge drift::", script, StringComparison.Ordinal);
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
