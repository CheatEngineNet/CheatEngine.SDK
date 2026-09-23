using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;
using CheatEngine.SDK.Repository.Tests.SourceScanning;

namespace CheatEngine.SDK.Repository.Tests.Abi;

/// <summary>
///     F02 is published in the repository before any support claim: the NativeAOT profile page names the Microsoft unload
///     restriction and every load profile, the classic export names carry the <c>FreeLibrary</c> caveat, the packed
///     README states the limits, and the NativeAOT loader harness never frees a mapped NativeAOT module (audit A00-07,
///     A04-03, A22-15, A23-F02-1..4, AX07-06, SRCREG-08).
/// </summary>
public sealed partial class NativeAotProfileDocumentTests
{
	private const string ProfilePage = "libs/CheatEngine.SDK.Abi/README.md";
	private const string UnloadRestriction = "https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries";

	[Fact]
	public void NativeAot_profile_page_cites_the_microsoft_unload_restriction_and_lists_every_load_profile()
	{
		string page = Read(ProfilePage);

		Assert.Contains(UnloadRestriction, page, StringComparison.Ordinal);
		Assert.Contains("FreeLibrary", page, StringComparison.Ordinal);
		Assert.Contains("ce-7.7.0.10621-x64-managed-hostfxr", page, StringComparison.Ordinal);
		Assert.Contains("9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d", page,
			StringComparison.Ordinal);
		Assert.Contains("LocalModified", page, StringComparison.Ordinal);
		foreach (string profile in (string[])
		         [
			         "| Historical CLR route", "| **Managed hostfxr route**", "| NativeAOT plugin DLL",
			         "| Classic native plugin exporting `CEPlugin_*`", "| x86 or ARM64 host"
		         ])
		{
			Assert.Contains(profile, page, StringComparison.Ordinal);
		}

		Assert.Contains("**Not supported**", page, StringComparison.Ordinal);
		Assert.Contains("never a Cheat Engine load success", page, StringComparison.Ordinal);
		Assert.Contains("Replacing the managed bootstrap with three `CEPlugin_*` exports is **not**", page,
			StringComparison.Ordinal);
		Assert.Contains("CESDK9102", page, StringComparison.Ordinal);
		Assert.Contains("CESDK0006", page, StringComparison.Ordinal);
		Assert.Contains("Q42", page, StringComparison.Ordinal);
		Assert.DoesNotContain("Status: placeholder", page, StringComparison.Ordinal);
	}

	[Fact]
	public void NativeExportNames_documentation_carries_the_FreeLibrary_caveat()
	{
		string source = Read("libs/CheatEngine.SDK.Abi/Native/NativeExportNames.cs");
		string summary = source[..source.IndexOf("public static class NativeExportNames", StringComparison.Ordinal)];

		Assert.Contains("FreeLibrary", summary, StringComparison.Ordinal);
		Assert.Contains("not a supported CheatEngine.SDK profile", summary, StringComparison.Ordinal);
		Assert.Contains("libs/CheatEngine.SDK.Abi/README.md", summary, StringComparison.Ordinal);
		Assert.Contains("Not a replacement for the managed bootstrap", summary, StringComparison.Ordinal);
		Assert.DoesNotContain("the path a Native AOT build of a plugin would take", source, StringComparison.Ordinal);
		Assert.DoesNotContain("Native (Native AOT) load path only",
			Read("libs/CheatEngine.SDK.Abi/Native/PluginType.cs"),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Packed_readme_states_the_load_profiles_and_links_the_restrictions()
	{
		string readme = Read("src/CheatEngine.SDK/README.md");

		Assert.Contains("## Load profiles and limits", readme, StringComparison.Ordinal);
		Assert.DoesNotContain("## AOT status", readme, StringComparison.Ordinal);
		Assert.Contains(
			"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/libs/CheatEngine.SDK.Abi/README.md", readme,
			StringComparison.Ordinal);
		Assert.Contains(UnloadRestriction, readme, StringComparison.Ordinal);
		Assert.Contains("| NativeAOT plugin DLL | Not supported", readme, StringComparison.Ordinal);
	}

	[Fact]
	public void Loader_harness_never_frees_a_mapped_nativeaot_module()
	{
		string[] sources = Directory.GetFiles(
			Path.Combine(RepositoryRoot.Path, "tests", "CheatEngine.SDK.NativeAotLoaderHarness"), "*.cs",
			SearchOption.TopDirectoryOnly);

		Assert.NotEmpty(sources);
		foreach (string file in sources)
		{
			string code = CSharpCode.BlankCommentsAndLiterals(File.ReadAllText(file));
			Assert.DoesNotMatch(FreeCall(), code);
		}

		Assert.Contains("library.unload=not-attempted",
			File.ReadAllText(Path.Combine(RepositoryRoot.Path, "tests", "CheatEngine.SDK.NativeAotLoaderHarness",
				"Program.cs")),
			StringComparison.Ordinal);
	}

	private static string Read(string relativePath)
	{
		return File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath))
			.Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	[GeneratedRegex(@"\bNativeLibrary\s*\.\s*Free\s*\(|\bFreeLibrary\s*\(|\bFreeLibraryAndExitThread\s*\(",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex FreeCall();
}
